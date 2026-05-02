using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TR.SimpleHttpServer.WebSocket;

/// <summary>
/// Represents an active WebSocket connection (RFC 6455)
/// </summary>
public class WebSocketConnection : IDisposable
{
	private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

	private readonly Stream _stream;
	private readonly WebSocketOptions _options;
	private readonly WebSocketFrameReader _frameReader;
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private bool _isOpen = true;
	private bool _closeSent = false;
	private bool _disposedValue;

	/// <summary>
	/// Gets whether the connection is currently open
	/// </summary>
	public bool IsOpen => _isOpen && !_disposedValue;

	/// <summary>
	/// The subprotocol that was negotiated during the WebSocket handshake, if any.
	/// </summary>
	public string? SelectedSubProtocol { get; }

	/// <summary>
	/// Occurs when a ping frame is received
	/// </summary>
	public event EventHandler<PingPongEventArgs>? PingReceived;

	/// <summary>
	/// Occurs when a pong frame is received
	/// </summary>
	public event EventHandler<PingPongEventArgs>? PongReceived;

	public WebSocketConnection(Stream stream)
		: this(stream, new WebSocketOptions(), null)
	{
	}

	public WebSocketConnection(Stream stream, WebSocketOptions options, string? selectedSubProtocol = null)
	{
		_stream = stream ?? throw new ArgumentNullException(nameof(stream));
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_frameReader = new WebSocketFrameReader(stream, options.MaxFrameSize);
		SelectedSubProtocol = selectedSubProtocol;
	}

	/// <summary>
	/// Receives a message from the WebSocket connection.
	/// </summary>
	/// <exception cref="WebSocketProtocolException">Thrown if the peer violates the WebSocket protocol; the connection is closed before the throw.</exception>
	public async Task<WebSocketMessage> ReceiveMessageAsync(CancellationToken cancellationToken)
	{
		ThrowIfDisposed();

		if (!_isOpen)
			throw new InvalidOperationException("Connection is not open");

		List<byte> messageData = [];
		WebSocketOpcode? messageType = null;

		while (true)
		{
			WebSocketFrame frame;
			try
			{
				frame = await _frameReader.ReadFrameAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (WebSocketFrameSizeException ex)
			{
				await FailConnectionAsync(WebSocketCloseStatus.MessageTooBig, ex.Message, cancellationToken).ConfigureAwait(false);
				throw new WebSocketProtocolException(WebSocketCloseStatus.MessageTooBig, ex.Message, ex);
			}

			// RFC 6455 5.2: RSV bits MUST be 0 unless an extension is negotiated.
			if (frame.Rsv1 || frame.Rsv2 || frame.Rsv3)
				await FailConnectionAsync(WebSocketCloseStatus.ProtocolError, "Reserved bits set without negotiated extension", cancellationToken).ConfigureAwait(false);

			// RFC 6455 5.1: client-to-server frames MUST be masked.
			if (_options.RequireMasking && !frame.IsMasked)
				await FailConnectionAsync(WebSocketCloseStatus.ProtocolError, "Client frame must be masked", cancellationToken).ConfigureAwait(false);

			bool isControl = ((int)frame.Opcode & 0x08) != 0;

			if (isControl)
			{
				// Reserved control opcodes 0xB-0xF
				if (frame.Opcode != WebSocketOpcode.Close
					&& frame.Opcode != WebSocketOpcode.Ping
					&& frame.Opcode != WebSocketOpcode.Pong)
				{
					await FailConnectionAsync(WebSocketCloseStatus.ProtocolError, $"Reserved control opcode 0x{(int)frame.Opcode:X}", cancellationToken).ConfigureAwait(false);
				}

				// RFC 6455 5.4: control frames MUST NOT be fragmented.
				if (!frame.IsFinal)
					await FailConnectionAsync(WebSocketCloseStatus.ProtocolError, "Control frame must not be fragmented", cancellationToken).ConfigureAwait(false);

				// RFC 6455 5.5: control frame payload <= 125 bytes.
				if (frame.Payload.Length > 125)
					await FailConnectionAsync(WebSocketCloseStatus.ProtocolError, "Control frame payload exceeds 125 bytes", cancellationToken).ConfigureAwait(false);

				if (frame.Opcode == WebSocketOpcode.Close)
				{
					return await HandleCloseFrameAsync(frame, cancellationToken).ConfigureAwait(false);
				}
				if (frame.Opcode == WebSocketOpcode.Ping)
				{
					await HandlePingFrameAsync(frame, cancellationToken).ConfigureAwait(false);
					continue;
				}
				// Pong
				PongReceived?.Invoke(this, new PingPongEventArgs(frame.Payload));
				continue;
			}

			// Data frame: validate opcode and continuation order
			if (frame.Opcode != WebSocketOpcode.Continuation
				&& frame.Opcode != WebSocketOpcode.Text
				&& frame.Opcode != WebSocketOpcode.Binary)
			{
				await FailConnectionAsync(WebSocketCloseStatus.ProtocolError, $"Reserved data opcode 0x{(int)frame.Opcode:X}", cancellationToken).ConfigureAwait(false);
			}

			if (frame.Opcode == WebSocketOpcode.Continuation)
			{
				if (messageType is null)
					await FailConnectionAsync(WebSocketCloseStatus.ProtocolError, "Continuation frame without initial frame", cancellationToken).ConfigureAwait(false);
			}
			else
			{
				if (messageType is not null)
					await FailConnectionAsync(WebSocketCloseStatus.ProtocolError, "New data frame received while expecting continuation", cancellationToken).ConfigureAwait(false);
				messageType = frame.Opcode;
			}

			// Enforce per-message size limit
			if ((long)messageData.Count + frame.Payload.Length > _options.MaxMessageSize)
			{
				await FailConnectionAsync(WebSocketCloseStatus.MessageTooBig, $"Message exceeds maximum size {_options.MaxMessageSize}", cancellationToken).ConfigureAwait(false);
			}

			messageData.AddRange(frame.Payload);

			if (!frame.IsFinal)
				continue;

			byte[] data = [.. messageData];

			if (messageType == WebSocketOpcode.Text)
			{
				if (_options.ValidateUtf8 && !IsValidUtf8(data))
					await FailConnectionAsync(WebSocketCloseStatus.InvalidPayloadData, "Text payload is not valid UTF-8", cancellationToken).ConfigureAwait(false);

				return new WebSocketMessage(WebSocketMessageType.Text, data);
			}

			return new WebSocketMessage(WebSocketMessageType.Binary, data);
		}
	}

	private async Task HandlePingFrameAsync(WebSocketFrame frame, CancellationToken cancellationToken)
	{
		// Send pong synchronously so any send error is observed and ordering is explicit.
		try
		{
			await SendFrameAsync(new WebSocketFrame(true, WebSocketOpcode.Pong, frame.Payload), cancellationToken).ConfigureAwait(false);
		}
		catch
		{
			_isOpen = false;
			throw;
		}

		PingReceived?.Invoke(this, new PingPongEventArgs(frame.Payload));
	}

	private async Task<WebSocketMessage> HandleCloseFrameAsync(WebSocketFrame frame, CancellationToken cancellationToken)
	{
		WebSocketCloseStatus? closeStatus = null;
		string closeReason = "";

		// RFC 6455 5.5.1: payload length must be 0, or >= 2 (status code) optionally followed by reason.
		if (frame.Payload.Length == 1)
		{
			await FailConnectionAsync(WebSocketCloseStatus.ProtocolError, "Close frame payload length 1 is invalid", cancellationToken).ConfigureAwait(false);
		}

		if (frame.Payload.Length >= 2)
		{
			ushort code = (ushort)((frame.Payload[0] << 8) | frame.Payload[1]);
			if (!IsValidCloseCode(code))
				await FailConnectionAsync(WebSocketCloseStatus.ProtocolError, $"Close frame uses disallowed status code {code}", cancellationToken).ConfigureAwait(false);

			closeStatus = (WebSocketCloseStatus)code;

			if (frame.Payload.Length > 2)
			{
				int reasonLen = frame.Payload.Length - 2;
				byte[] reasonBytes = new byte[reasonLen];
				Array.Copy(frame.Payload, 2, reasonBytes, 0, reasonLen);
				if (_options.ValidateUtf8 && !IsValidUtf8(reasonBytes))
					await FailConnectionAsync(WebSocketCloseStatus.InvalidPayloadData, "Close reason is not valid UTF-8", cancellationToken).ConfigureAwait(false);
				closeReason = StrictUtf8.GetString(reasonBytes);
			}
		}

		_isOpen = false;

		if (_options.AutoRespondToClose && !_closeSent)
		{
			try
			{
				WebSocketCloseStatus replyStatus = closeStatus ?? WebSocketCloseStatus.NormalClosure;
				await SendCloseFrameAsync(replyStatus, "", cancellationToken).ConfigureAwait(false);
			}
			catch
			{
				// Best effort - peer may have already closed the underlying stream.
			}
		}

		return new WebSocketMessage(WebSocketMessageType.Close, Array.Empty<byte>(), closeStatus, closeReason);
	}

	/// <summary>
	/// Sends a text message.
	/// </summary>
	public Task SendTextAsync(string message, CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		byte[] data = Encoding.UTF8.GetBytes(message);
		return SendFrameAsync(new WebSocketFrame(true, WebSocketOpcode.Text, data), cancellationToken);
	}

	/// <summary>
	/// Sends a binary message.
	/// </summary>
	public Task SendBinaryAsync(byte[] data, CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		return SendFrameAsync(new WebSocketFrame(true, WebSocketOpcode.Binary, data), cancellationToken);
	}

	/// <summary>
	/// Sends a ping frame.
	/// </summary>
	public Task SendPingAsync(byte[] data, CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		if (data.Length > 125)
			throw new ArgumentException("Ping payload must not exceed 125 bytes", nameof(data));
		return SendFrameAsync(new WebSocketFrame(true, WebSocketOpcode.Ping, data), cancellationToken);
	}

	/// <summary>
	/// Closes the WebSocket connection.
	/// </summary>
	public async Task CloseAsync(WebSocketCloseStatus status, string reason, CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		if (_closeSent)
			return;
		await SendCloseFrameAsync(status, reason, cancellationToken).ConfigureAwait(false);
	}

	private async Task SendCloseFrameAsync(WebSocketCloseStatus status, string reason, CancellationToken cancellationToken)
	{
		byte[] reasonBytes = string.IsNullOrEmpty(reason) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(reason);
		if (reasonBytes.Length > 123)
			throw new ArgumentException("Close reason is too long (max 123 bytes when serialized as UTF-8)", nameof(reason));

		byte[] payload = new byte[2 + reasonBytes.Length];
		payload[0] = (byte)(((int)status >> 8) & 0xFF);
		payload[1] = (byte)((int)status & 0xFF);
		if (reasonBytes.Length > 0)
			Array.Copy(reasonBytes, 0, payload, 2, reasonBytes.Length);

		await SendFrameAsync(new WebSocketFrame(true, WebSocketOpcode.Close, payload), cancellationToken).ConfigureAwait(false);
		_closeSent = true;
		_isOpen = false;
	}

	private async Task SendFrameAsync(WebSocketFrame frame, CancellationToken cancellationToken)
	{
		await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			byte[] frameBytes = frame.ToBytes();
			await _stream.WriteAsync(frameBytes, 0, frameBytes.Length, cancellationToken).ConfigureAwait(false);
			await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_sendLock.Release();
		}
	}

	private async Task FailConnectionAsync(WebSocketCloseStatus status, string description, CancellationToken cancellationToken)
	{
		try
		{
			if (!_disposedValue && !_closeSent)
			{
				await SendCloseFrameAsync(status, TruncateReasonForCloseFrame(description), cancellationToken).ConfigureAwait(false);
			}
		}
		catch
		{
			// Best effort - peer may have closed already.
		}
		finally
		{
			_isOpen = false;
		}

		throw new WebSocketProtocolException(status, description);
	}

	/// <summary>
	/// Trims <paramref name="reason"/> to fit the 123-byte UTF-8 close-reason limit, splitting at a
	/// codepoint boundary so a multi-byte character is never cut in half.
	/// </summary>
	private static string TruncateReasonForCloseFrame(string reason)
	{
		if (string.IsNullOrEmpty(reason)) return "";
		byte[] bytes = Encoding.UTF8.GetBytes(reason);
		if (bytes.Length <= 123) return reason;

		// Walk back from byte 123 until we land on a UTF-8 leading byte (0xxxxxxx or 11xxxxxx).
		int cut = 123;
		while (cut > 0 && (bytes[cut] & 0xC0) == 0x80)
			cut--;
		return Encoding.UTF8.GetString(bytes, 0, cut);
	}

	private static bool IsValidUtf8(byte[] data)
	{
		try
		{
			StrictUtf8.GetCharCount(data);
			return true;
		}
		catch (DecoderFallbackException)
		{
			return false;
		}
	}

	/// <summary>
	/// Returns true if <paramref name="code"/> is a valid close-frame status code per RFC 6455 7.4.
	/// </summary>
	private static bool IsValidCloseCode(ushort code)
	{
		// Reserved range: 0-999
		if (code < 1000) return false;
		// 1000-1011 defined; 1004 reserved/unassigned, 1005/1006 must not appear in frames
		if (code <= 1011)
		{
			return code != 1004 && code != 1005 && code != 1006;
		}
		// 1012, 1013, 1014 are IANA-registered (Service Restart / Try Again Later / Bad Gateway)
		if (code <= 1014) return true;
		// 1015 (TLS handshake) must not appear in frames
		if (code == 1015) return false;
		// 1016-2999 reserved
		if (code <= 2999) return false;
		// 3000-3999 framework registered, 4000-4999 application private use
		if (code <= 4999) return true;
		return false;
	}

	private void ThrowIfDisposed()
	{
		if (_disposedValue)
			throw new ObjectDisposedException(nameof(WebSocketConnection));
	}

	#region IDisposable Support
	protected virtual void Dispose(bool disposing)
	{
		if (!_disposedValue)
		{
			if (disposing)
			{
				_isOpen = false;
				_sendLock.Dispose();
			}
			_disposedValue = true;
		}
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
	#endregion
}

/// <summary>
/// Event arguments for ping/pong frame reception events
/// </summary>
public class PingPongEventArgs(byte[] payload) : EventArgs
{
	/// <summary>
	/// Gets the payload data from the ping/pong frame
	/// </summary>
	public byte[] Payload { get; } = payload;
}
