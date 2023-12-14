using System;
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

		byte[] lineBuffer = [];
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

			lineBuffer = new byte[dataLengthInBuffer];
			Array.Copy(buffer, currentBufferTop, lineBuffer, 0, dataLengthInBuffer);
			currentBufferTop = 0;
			dataLengthInBuffer = 0;
		}

		while (true)
		{
			byte[] bufTmp = new byte[DefaultBufferSize];
			int bytesRead = await ReadIfAvailableAsync(bufTmp, 0, bufTmp.Length, cancellationToken);
			if (bytesRead <= 0)
			{
				if (lineBuffer.Length == 0)
					return "";

				return encoding.GetString(lineBuffer);
			}

			for (int i = 0; i < bytesRead; i++)
			{
				byte c = bufTmp[i];
				if (c == CR || c == LF)
				{
					bool isCRLF = c == CR && i + 1 < bytesRead && bufTmp[i + 1] == LF;
					int lineLength = i;
					byte[] lineBytes = new byte[lineBuffer.Length + lineLength];
					Array.Copy(lineBuffer, 0, lineBytes, 0, lineBuffer.Length);
					Array.Copy(bufTmp, 0, lineBytes, lineBuffer.Length, lineLength);
					string line = encoding.GetString(lineBytes, 0, lineBytes.Length);

					int lineEndingLength = isCRLF ? 2 : 1;
					int remainingBytes = bytesRead - i - lineEndingLength;
					if (0 < remainingBytes)
					{
						dataLengthInBuffer = remainingBytes;
						currentBufferTop = 0;
						Array.Copy(bufTmp, i + lineEndingLength, buffer, 0, remainingBytes);
					}
					return line;
				}
			}

			byte[] lineBufferForNextLoop = new byte[lineBuffer.Length + bytesRead];
			Array.Copy(lineBuffer, 0, lineBufferForNextLoop, 0, lineBuffer.Length);
			Array.Copy(bufTmp, 0, lineBufferForNextLoop, lineBuffer.Length, bytesRead);
			lineBuffer = lineBufferForNextLoop;
		}
	}

	public async Task<byte[]> ReadRemainingBytesAsync()
	{
		if (!stream.CanRead)
			throw new InvalidOperationException("Stream is not readable.");

		cancellationToken.ThrowIfCancellationRequested();

		byte[] byteArrayToReturn = new byte[dataLengthInBuffer];
		Array.Copy(buffer, currentBufferTop, byteArrayToReturn, 0, dataLengthInBuffer);
		while (true)
		{
			byte[] tmp = new byte[DefaultBufferSize];
			int bytesRead = await ReadIfAvailableAsync(tmp, 0, tmp.Length, false, cancellationToken);
			if (bytesRead <= 0)
				return byteArrayToReturn;

			byte[] byteArrayToReturnForNextLoop = new byte[byteArrayToReturn.Length + bytesRead];
			Array.Copy(byteArrayToReturn, 0, byteArrayToReturnForNextLoop, 0, byteArrayToReturn.Length);
			Array.Copy(tmp, 0, byteArrayToReturnForNextLoop, byteArrayToReturn.Length, bytesRead);
			byteArrayToReturn = byteArrayToReturnForNextLoop;
		}
	}
}
