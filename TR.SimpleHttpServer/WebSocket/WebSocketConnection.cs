using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TR.SimpleHttpServer.WebSocket;

/// <summary>
/// Represents an active WebSocket connection
/// </summary>
public class WebSocketConnection(
	Stream stream
) : IDisposable
{
	private readonly Stream _stream = stream ?? throw new ArgumentNullException(nameof(stream));
	private readonly WebSocketFrameReader _frameReader = new(stream);
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private bool _isOpen = true;
	private bool _closeSent = false;
	private bool _disposedValue;

	/// <summary>
	/// Gets whether the connection is currently open
	/// </summary>
	public bool IsOpen => _isOpen && !_disposedValue;

	/// <summary>
	/// Occurs when a ping frame is received
	/// </summary>
	public event EventHandler<PingPongEventArgs>? PingReceived;

	/// <summary>
	/// Occurs when a pong frame is received
	/// </summary>
	public event EventHandler<PingPongEventArgs>? PongReceived;

	/// <summary>
	/// Receives a message from the WebSocket connection
	/// </summary>
	public async Task<WebSocketMessage> ReceiveMessageAsync(CancellationToken cancellationToken)
	{
		ThrowIfDisposed();

		if (!_isOpen)
			throw new InvalidOperationException("Connection is not open");

		List<byte> messageData = [];
		WebSocketOpcode? messageType = null;

		while (true)
		{
			WebSocketFrame frame = await _frameReader.ReadFrameAsync(cancellationToken).ConfigureAwait(false);

			var result = await ProcessFrameAsync(frame, messageType, messageData, cancellationToken).ConfigureAwait(false);

			if (result.IsComplete)
			{
				return result.Message!;
			}

			if (result.UpdatedMessageType.HasValue)
			{
				messageType = result.UpdatedMessageType;
			}
		}
	}

	/// <summary>
	/// Processes a single WebSocket frame
	/// </summary>
	private async Task<FrameProcessResult> ProcessFrameAsync(
		WebSocketFrame frame,
		WebSocketOpcode? currentMessageType,
		List<byte> messageData,
		CancellationToken cancellationToken
	)
	{
		return frame.Opcode switch
		{
			WebSocketOpcode.Close => ProcessCloseFrame(frame),
			WebSocketOpcode.Ping => ProcessPingFrameAsync(frame, cancellationToken),
			WebSocketOpcode.Pong => ProcessPongFrame(frame),
			WebSocketOpcode.Continuation or WebSocketOpcode.Text or WebSocketOpcode.Binary
				=> ProcessDataFrame(frame, currentMessageType, messageData),

			_ => throw new InvalidOperationException($"Unknown opcode: {frame.Opcode}")
		};
	}

	private FrameProcessResult ProcessCloseFrame(WebSocketFrame frame)
	{
		_isOpen = false;
		WebSocketCloseStatus? closeStatus = null;
		string closeReason = "";

		if (frame.Payload.Length >= 2)
		{
			closeStatus = (WebSocketCloseStatus)((frame.Payload[0] << 8) | frame.Payload[1]);
			if (frame.Payload.Length > 2)
			{
				closeReason = Encoding.UTF8.GetString(frame.Payload, 2, frame.Payload.Length - 2);
			}
		}

		var message = new WebSocketMessage(WebSocketMessageType.Close, Array.Empty<byte>(), closeStatus, closeReason);
		return new FrameProcessResult { Message = message, IsComplete = true };
	}

	private FrameProcessResult ProcessPingFrameAsync(WebSocketFrame frame, CancellationToken cancellationToken)
	{
		// Respond with Pong without awaiting - it can complete in the background
		_ = SendPongAsync(frame.Payload, cancellationToken);

		// Invoke PingReceived event
		PingReceived?.Invoke(this, new PingPongEventArgs(frame.Payload));

		return new FrameProcessResult { IsComplete = false };
	}

	private FrameProcessResult ProcessPongFrame(WebSocketFrame frame)
	{
		// Invoke PongReceived event
		PongReceived?.Invoke(this, new PingPongEventArgs(frame.Payload));

		// Pong received, continue waiting for data frames
		return new FrameProcessResult { IsComplete = false };
	}

	private FrameProcessResult ProcessDataFrame(WebSocketFrame frame, WebSocketOpcode? messageType, List<byte> messageData)
	{
		if (frame.Opcode == WebSocketOpcode.Continuation)
		{
			if (messageType is null)
				throw new InvalidOperationException("Received continuation frame without initial frame");
		}
		else
		{
			// This is the first frame of a new message
			messageType = frame.Opcode;
		}

		// Accumulate payload
		messageData.AddRange(frame.Payload);

		// If this is the final frame, return the message
		if (frame.IsFinal)
		{
			WebSocketMessageType type = messageType == WebSocketOpcode.Text
				? WebSocketMessageType.Text
				: WebSocketMessageType.Binary;

			var message = new WebSocketMessage(type, [.. messageData]);
			return new FrameProcessResult { Message = message, IsComplete = true };
		}

		return new FrameProcessResult { IsComplete = false, UpdatedMessageType = messageType };
	}

	private class FrameProcessResult
	{
		public WebSocketMessage? Message { get; set; }
		public bool IsComplete { get; set; }
		public WebSocketOpcode? UpdatedMessageType { get; set; }
	}

	/// <summary>
	/// Sends a text message to the WebSocket connection
	/// </summary>
	public Task SendTextAsync(string message, CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		byte[] data = Encoding.UTF8.GetBytes(message);
		return SendFrameAsync(new WebSocketFrame(true, WebSocketOpcode.Text, data), cancellationToken);
	}

	/// <summary>
	/// Sends a binary message to the WebSocket connection
	/// </summary>
	public Task SendBinaryAsync(byte[] data, CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		return SendFrameAsync(new WebSocketFrame(true, WebSocketOpcode.Binary, data), cancellationToken);
	}

	/// <summary>
	/// Sends a ping frame
	/// </summary>
	public Task SendPingAsync(byte[] data, CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		return SendFrameAsync(new WebSocketFrame(true, WebSocketOpcode.Ping, data), cancellationToken);
	}

	/// <summary>
	/// Sends a pong frame
	/// </summary>
	private Task SendPongAsync(byte[] data, CancellationToken cancellationToken)
	{
		ThrowIfDisposed();
		return SendFrameAsync(new WebSocketFrame(true, WebSocketOpcode.Pong, data), cancellationToken);
	}

	/// <summary>
	/// Closes the WebSocket connection
	/// </summary>
	public async Task CloseAsync(WebSocketCloseStatus status, string reason, CancellationToken cancellationToken)
	{
		ThrowIfDisposed();

		if (_closeSent)
			return;

		byte[] statusBytes = new byte[2]
		{
			(byte)(((int)status >> 8) & 0xFF),
			(byte)((int)status & 0xFF)
		};

		byte[] reasonBytes = string.IsNullOrEmpty(reason) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(reason);
		byte[] payload = new byte[statusBytes.Length + reasonBytes.Length];
		Array.Copy(statusBytes, 0, payload, 0, statusBytes.Length);
		Array.Copy(reasonBytes, 0, payload, statusBytes.Length, reasonBytes.Length);

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
#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
			await _stream.WriteAsync(frameBytes.AsMemory(), cancellationToken).ConfigureAwait(false);
#else
			await _stream.WriteAsync(frameBytes, 0, frameBytes.Length, cancellationToken).ConfigureAwait(false);
#endif
			await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_sendLock.Release();
		}
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
