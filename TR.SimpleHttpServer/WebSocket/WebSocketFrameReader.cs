using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TR.SimpleHttpServer.WebSocket;

/// <summary>
/// Reads WebSocket frames from a stream
/// </summary>
public class WebSocketFrameReader(
	Stream stream
)
{
	private readonly Stream _stream = stream ?? throw new ArgumentNullException(nameof(stream));

	/// <summary>
	/// Reads a WebSocket frame from the stream
	/// </summary>
	public async Task<WebSocketFrame> ReadFrameAsync(CancellationToken cancellationToken)
	{
		// Read first two bytes
		byte[] header = new byte[2];
		await ReadExactAsync(header, 0, 2, cancellationToken).ConfigureAwait(false);

		bool isFinal = (header[0] & 0x80) != 0;
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
		}

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

		return new WebSocketFrame(isFinal, opcode, isMasked, maskingKey, payload);
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
