using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TR.SimpleHttpServer;

internal class MyStreamReader(
	NetworkStream stream,
	CancellationToken cancellationToken,
	Encoding encoding
)
{
	static readonly int DefaultBufferSize = 4096;
	private readonly NetworkStream stream = stream;
	private readonly CancellationToken cancellationToken = cancellationToken;
	private readonly Encoding encoding = encoding;
	private readonly byte[] buffer = new byte[DefaultBufferSize];
	private int currentBufferTop = 0;
	private int dataLengthInBuffer = 0;

	const char CR = '\r';
	const char LF = '\n';

	private Task<int> ReadIfAvailableAsync(byte[] buffer, int offset, int count, bool forceRead, CancellationToken cancellationToken)
	 => forceRead || stream.DataAvailable ? stream.ReadAsync(buffer, offset, count, cancellationToken) : Task.FromResult(0);

	public async Task<string> ReadLineAsync(bool forceRead = false)
	{
		if (!stream.CanRead)
			throw new InvalidOperationException("Stream is not readable.");

		cancellationToken.ThrowIfCancellationRequested();

		using MemoryStream lineBuffer = new(DefaultBufferSize);
		if (dataLengthInBuffer != 0)
		{
			int iLimit = currentBufferTop + dataLengthInBuffer;
			for (int i = currentBufferTop; i < iLimit; i++)
			{
				byte c = buffer[i];
				if (c == CR || c == LF)
				{
					bool isCRLF = c == CR && i + 1 < iLimit && buffer[i + 1] == LF;
					int lineLength = i - currentBufferTop;
					string line = encoding.GetString(buffer, currentBufferTop, lineLength);

					int lineEndingLength = isCRLF ? 2 : 1;
					dataLengthInBuffer -= lineLength + lineEndingLength;
					currentBufferTop = i + lineEndingLength;
					return line;
				}
			}

			lineBuffer.Write(buffer, currentBufferTop, dataLengthInBuffer);
			currentBufferTop = 0;
			dataLengthInBuffer = 0;
		}

		while (true)
		{
			int bytesRead = await ReadIfAvailableAsync(buffer, 0, buffer.Length, forceRead, cancellationToken);
			if (bytesRead <= 0)
			{
				if (lineBuffer.Length == 0)
					return "";

				return encoding.GetString(lineBuffer.GetBuffer(), 0, (int)lineBuffer.Length);
			}

			for (int i = 0; i < bytesRead; i++)
			{
				byte c = buffer[i];
				if (c == CR || c == LF)
				{
					bool isCRLF = c == CR && i + 1 < bytesRead && buffer[i + 1] == LF;
					int lineLength = i;
					lineBuffer.Write(buffer, 0, lineLength);
					string line = encoding.GetString(lineBuffer.GetBuffer(), 0, (int)lineBuffer.Length);

					int lineEndingLength = isCRLF ? 2 : 1;
					int remainingBytes = bytesRead - i - lineEndingLength;
					if (0 < remainingBytes)
					{
						dataLengthInBuffer = remainingBytes;
						currentBufferTop = 0;
						Array.Copy(buffer, i + lineEndingLength, this.buffer, 0, remainingBytes);
					}
					return line;
				}
			}

			lineBuffer.Write(buffer, 0, bytesRead);
		}
	}

	public async Task<byte[]> ReadRemainingBytesAsync()
	{
		if (!stream.CanRead)
			throw new InvalidOperationException("Stream is not readable.");

		cancellationToken.ThrowIfCancellationRequested();

		using MemoryStream result = new();
		if (dataLengthInBuffer > 0)
		{
			result.Write(buffer, currentBufferTop, dataLengthInBuffer);
		}

		while (true)
		{
			int bytesRead = await ReadIfAvailableAsync(buffer, 0, buffer.Length, false, cancellationToken);
			if (bytesRead <= 0)
				return result.ToArray();

			result.Write(buffer, 0, bytesRead);
		}
	}
}
