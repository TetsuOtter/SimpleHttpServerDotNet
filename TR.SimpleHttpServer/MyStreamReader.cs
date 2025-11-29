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

	const byte CR = (byte)'\r';
	const byte LF = (byte)'\n';

#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
	private ValueTask<int> ReadIfAvailableAsync(Memory<byte> buffer, bool forceRead, CancellationToken cancellationToken)
		=> forceRead || stream.DataAvailable ? stream.ReadAsync(buffer, cancellationToken) : new ValueTask<int>(0);
#else
	private Task<int> ReadIfAvailableAsync(byte[] buffer, int offset, int count, bool forceRead, CancellationToken cancellationToken)
	 => forceRead || stream.DataAvailable ? stream.ReadAsync(buffer, offset, count, cancellationToken) : Task.FromResult(0);
#endif

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
#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
					string line = encoding.GetString(buffer.AsSpan(currentBufferTop, lineLength));
#else
					string line = encoding.GetString(buffer, currentBufferTop, lineLength);
#endif

					int lineEndingLength = isCRLF ? 2 : 1;
					dataLengthInBuffer -= lineLength + lineEndingLength;
					currentBufferTop = i + lineEndingLength;
					return line;
				}
			}

#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
			lineBuffer.Write(buffer.AsSpan(currentBufferTop, dataLengthInBuffer));
#else
			lineBuffer.Write(buffer, currentBufferTop, dataLengthInBuffer);
#endif
			currentBufferTop = 0;
			dataLengthInBuffer = 0;
		}

		while (true)
		{
#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
			int bytesRead = await ReadIfAvailableAsync(buffer.AsMemory(), forceRead, cancellationToken);
#else
			int bytesRead = await ReadIfAvailableAsync(buffer, 0, buffer.Length, forceRead, cancellationToken);
#endif
			if (bytesRead <= 0)
			{
				if (lineBuffer.Length == 0)
					return "";

#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
				return encoding.GetString(lineBuffer.GetBuffer().AsSpan(0, (int)lineBuffer.Length));
#else
				return encoding.GetString(lineBuffer.GetBuffer(), 0, (int)lineBuffer.Length);
#endif
			}

			for (int i = 0; i < bytesRead; i++)
			{
				byte c = buffer[i];
				if (c == CR || c == LF)
				{
					bool isCRLF = c == CR && i + 1 < bytesRead && buffer[i + 1] == LF;
					int lineLength = i;
#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
					lineBuffer.Write(buffer.AsSpan(0, lineLength));
					string line = encoding.GetString(lineBuffer.GetBuffer().AsSpan(0, (int)lineBuffer.Length));
#else
					lineBuffer.Write(buffer, 0, lineLength);
					string line = encoding.GetString(lineBuffer.GetBuffer(), 0, (int)lineBuffer.Length);
#endif

					int lineEndingLength = isCRLF ? 2 : 1;
					int remainingBytes = bytesRead - i - lineEndingLength;
					if (0 < remainingBytes)
					{
						dataLengthInBuffer = remainingBytes;
						currentBufferTop = 0;
#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
						buffer.AsSpan(i + lineEndingLength, remainingBytes).CopyTo(this.buffer.AsSpan());
#else
						Array.Copy(buffer, i + lineEndingLength, this.buffer, 0, remainingBytes);
#endif
					}
					return line;
				}
			}

#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
			lineBuffer.Write(buffer.AsSpan(0, bytesRead));
#else
			lineBuffer.Write(buffer, 0, bytesRead);
#endif
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
#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
			result.Write(buffer.AsSpan(currentBufferTop, dataLengthInBuffer));
#else
			result.Write(buffer, currentBufferTop, dataLengthInBuffer);
#endif
		}

		while (true)
		{
#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
			int bytesRead = await ReadIfAvailableAsync(buffer.AsMemory(), false, cancellationToken);
#else
			int bytesRead = await ReadIfAvailableAsync(buffer, 0, buffer.Length, false, cancellationToken);
#endif
			if (bytesRead <= 0)
				return result.ToArray();

#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
			result.Write(buffer.AsSpan(0, bytesRead));
#else
			result.Write(buffer, 0, bytesRead);
#endif
		}
	}
}
