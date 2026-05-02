using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TR.SimpleHttpServer.WebSocket;

/// <summary>
/// Reads WebSocket frames from a stream
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public class WebSocketFrameReader
{
	private readonly Stream _stream;
	private readonly long _maxFrameSize;

	public WebSocketFrameReader(Stream stream)
		: this(stream, WebSocketOptions.DefaultMaxFrameSize)
	{
	}

	public WebSocketFrameReader(Stream stream, long maxFrameSize)
	{
		_stream = stream ?? throw new ArgumentNullException(nameof(stream));
		if (maxFrameSize < 0) throw new ArgumentOutOfRangeException(nameof(maxFrameSize));
		_maxFrameSize = maxFrameSize;
	}

	/// <summary>
	/// Reads a WebSocket frame from the stream
	/// </summary>
	public async Task<WebSocketFrame> ReadFrameAsync(CancellationToken cancellationToken)
	{
		// Read first two bytes
		byte[] header = new byte[2];
		await ReadExactAsync(header, 0, 2, cancellationToken).ConfigureAwait(false);

		bool isFinal = (header[0] & 0x80) != 0;
		bool rsv1 = (header[0] & 0x40) != 0;
		bool rsv2 = (header[0] & 0x20) != 0;
		bool rsv3 = (header[0] & 0x10) != 0;
		WebSocketOpcode opcode = (WebSocketOpcode)(header[0] & 0x0F);
		bool isMasked = (header[1] & 0x80) != 0;
		long payloadLength = header[1] & 0x7F;

		// Read extended payload length if needed
		if (payloadLength == 126)
		{
			byte[] extLen = new byte[2];
			await ReadExactAsync(extLen, 0, 2, cancellationToken).ConfigureAwait(false);
			payloadLength = (extLen[0] << 8) | extLen[1];
		}
		else if (payloadLength == 127)
		{
			byte[] extLen = new byte[8];
			await ReadExactAsync(extLen, 0, 8, cancellationToken).ConfigureAwait(false);
			payloadLength = 0;
			for (int i = 0; i < 8; i++)
			{
				payloadLength = (payloadLength << 8) | extLen[i];
			}

			// RFC 6455: most-significant bit of 64-bit length MUST be 0
			if (payloadLength < 0)
				throw new WebSocketFrameSizeException("Frame payload length has the high bit set (RFC 6455 violation)");
		}

		// Enforce configured limit before allocating
		if (payloadLength > _maxFrameSize || payloadLength > int.MaxValue)
			throw new WebSocketFrameSizeException($"Frame payload length {payloadLength} exceeds maximum allowed size {_maxFrameSize}");

		// Read masking key if masked
		byte[] maskingKey = Array.Empty<byte>();
		if (isMasked)
		{
			maskingKey = new byte[4];
			await ReadExactAsync(maskingKey, 0, 4, cancellationToken).ConfigureAwait(false);
		}

		// Read payload
		byte[] payload = new byte[payloadLength];
		if (payloadLength > 0)
		{
			await ReadExactAsync(payload, 0, (int)payloadLength, cancellationToken).ConfigureAwait(false);

			// Unmask payload if masked
			if (isMasked)
			{
				payload = WebSocketFrame.ApplyMask(payload, maskingKey);
			}
		}

		return new WebSocketFrame(isFinal, rsv1, rsv2, rsv3, opcode, isMasked, maskingKey, payload);
	}

	private async Task ReadExactAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
	{
		int totalRead = 0;
		while (totalRead < count)
		{
			int bytesRead = await _stream.ReadAsync(buffer, offset + totalRead, count - totalRead, cancellationToken).ConfigureAwait(false);
			if (bytesRead == 0)
			{
				throw new EndOfStreamException("Unexpected end of stream while reading WebSocket frame");
			}
			totalRead += bytesRead;
		}
	}
}

/// <summary>
/// Thrown when a frame's declared payload length exceeds the configured maximum.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public class WebSocketFrameSizeException : Exception
{
	public WebSocketFrameSizeException(string message) : base(message) { }
}
