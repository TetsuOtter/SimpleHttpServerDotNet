using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using TR.SimpleHttpServer.WebSocket;

namespace TR.SimpleHttpServer.Tests;

public class WebSocketFrameReaderTests
{
	[Fact]
	public async Task ReadFrameAsync_UnmaskedTextFrame_ParsesCorrectly()
	{
		// Arrange - Create a text frame with "Hello"
		byte[] frameBytes = new byte[]
		{
			0x81, // FIN=1, Opcode=1 (text)
			0x05, // MASK=0, Length=5
			0x48, 0x65, 0x6C, 0x6C, 0x6F // "Hello"
		};

		using MemoryStream stream = new(frameBytes);
		WebSocketFrameReader reader = new(stream);

		// Act
		WebSocketFrame frame = await reader.ReadFrameAsync(CancellationToken.None);

		// Assert
		Assert.True(frame.IsFinal);
		Assert.Equal(WebSocketOpcode.Text, frame.Opcode);
		Assert.False(frame.IsMasked);
		Assert.Equal("Hello", System.Text.Encoding.UTF8.GetString(frame.Payload));
	}

	[Fact]
	public async Task ReadFrameAsync_MaskedTextFrame_UnmasksCorrectly()
	{
		// Arrange - Create a masked frame
		byte[] maskingKey = new byte[] { 0x37, 0xFA, 0x21, 0x3D };
		byte[] payload = System.Text.Encoding.UTF8.GetBytes("Hello");
		byte[] maskedPayload = WebSocketFrame.ApplyMask(payload, maskingKey);

		using MemoryStream stream = new();
		stream.WriteByte(0x81); // FIN=1, Opcode=1 (text)
		stream.WriteByte((byte)(0x80 | payload.Length)); // MASK=1, Length
		stream.Write(maskingKey, 0, 4);
		stream.Write(maskedPayload, 0, maskedPayload.Length);
		stream.Position = 0;

		WebSocketFrameReader reader = new(stream);

		// Act
		WebSocketFrame frame = await reader.ReadFrameAsync(CancellationToken.None);

		// Assert
		Assert.True(frame.IsFinal);
		Assert.Equal(WebSocketOpcode.Text, frame.Opcode);
		Assert.True(frame.IsMasked);
		Assert.Equal("Hello", System.Text.Encoding.UTF8.GetString(frame.Payload));
	}

	[Fact]
	public async Task ReadFrameAsync_BinaryFrame_ParsesCorrectly()
	{
		// Arrange
		byte[] payload = new byte[] { 0x01, 0x02, 0x03 };
		byte[] frameBytes = new byte[] { 0x82, 0x03, 0x01, 0x02, 0x03 };

		using MemoryStream stream = new(frameBytes);
		WebSocketFrameReader reader = new(stream);

		// Act
		WebSocketFrame frame = await reader.ReadFrameAsync(CancellationToken.None);

		// Assert
		Assert.Equal(WebSocketOpcode.Binary, frame.Opcode);
		Assert.Equal(payload, frame.Payload);
	}

	[Fact]
	public async Task ReadFrameAsync_PingFrame_ParsesCorrectly()
	{
		// Arrange
		byte[] frameBytes = new byte[] { 0x89, 0x00 }; // Ping with no payload

		using MemoryStream stream = new(frameBytes);
		WebSocketFrameReader reader = new(stream);

		// Act
		WebSocketFrame frame = await reader.ReadFrameAsync(CancellationToken.None);

		// Assert
		Assert.True(frame.IsFinal);
		Assert.Equal(WebSocketOpcode.Ping, frame.Opcode);
		Assert.Empty(frame.Payload);
	}

	[Fact]
	public async Task ReadFrameAsync_PongFrame_ParsesCorrectly()
	{
		// Arrange
		byte[] payload = new byte[] { 0xAA };
		byte[] frameBytes = new byte[] { 0x8A, 0x01, 0xAA }; // Pong with 1 byte payload

		using MemoryStream stream = new(frameBytes);
		WebSocketFrameReader reader = new(stream);

		// Act
		WebSocketFrame frame = await reader.ReadFrameAsync(CancellationToken.None);

		// Assert
		Assert.Equal(WebSocketOpcode.Pong, frame.Opcode);
		Assert.Equal(payload, frame.Payload);
	}

	[Fact]
	public async Task ReadFrameAsync_CloseFrame_ParsesCorrectly()
	{
		// Arrange - Close frame with status 1000
		byte[] frameBytes = new byte[] { 0x88, 0x02, 0x03, 0xE8 };

		using MemoryStream stream = new(frameBytes);
		WebSocketFrameReader reader = new(stream);

		// Act
		WebSocketFrame frame = await reader.ReadFrameAsync(CancellationToken.None);

		// Assert
		Assert.Equal(WebSocketOpcode.Close, frame.Opcode);
		Assert.Equal(new byte[] { 0x03, 0xE8 }, frame.Payload);
	}

	[Fact]
	public async Task ReadFrameAsync_NonFinalFrame_ParsesCorrectly()
	{
		// Arrange
		byte[] frameBytes = new byte[] { 0x01, 0x03, 0x41, 0x42, 0x43 }; // FIN=0, Text, "ABC"

		using MemoryStream stream = new(frameBytes);
		WebSocketFrameReader reader = new(stream);

		// Act
		WebSocketFrame frame = await reader.ReadFrameAsync(CancellationToken.None);

		// Assert
		Assert.False(frame.IsFinal);
		Assert.Equal(WebSocketOpcode.Text, frame.Opcode);
	}

	[Fact]
	public async Task ReadFrameAsync_ExtendedLength16Bit_ParsesCorrectly()
	{
		// Arrange - Frame with 200 bytes payload (requires 16-bit extended length)
		byte[] payload = new byte[200];
		for (int i = 0; i < payload.Length; i++)
			payload[i] = (byte)(i % 256);

		using MemoryStream stream = new();
		stream.WriteByte(0x82); // FIN=1, Binary
		stream.WriteByte(126); // Extended length indicator
		stream.WriteByte(0); // Length high byte
		stream.WriteByte(200); // Length low byte
		stream.Write(payload, 0, payload.Length);
		stream.Position = 0;

		WebSocketFrameReader reader = new(stream);

		// Act
		WebSocketFrame frame = await reader.ReadFrameAsync(CancellationToken.None);

		// Assert
		Assert.Equal(WebSocketOpcode.Binary, frame.Opcode);
		Assert.Equal(200, frame.Payload.Length);
		Assert.Equal(payload, frame.Payload);
	}

	[Fact]
	public async Task ReadFrameAsync_ExtendedLength64Bit_ParsesCorrectly()
	{
		// Arrange - Frame with 70000 bytes payload (requires 64-bit extended length)
		byte[] payload = new byte[70000];
		for (int i = 0; i < payload.Length; i++)
			payload[i] = (byte)(i % 256);

		using MemoryStream stream = new();
		stream.WriteByte(0x82); // FIN=1, Binary
		stream.WriteByte(127); // 64-bit extended length indicator
													 // Length in 8 bytes (big-endian)
		stream.WriteByte(0);
		stream.WriteByte(0);
		stream.WriteByte(0);
		stream.WriteByte(0);
		stream.WriteByte(0);
		stream.WriteByte(0x01);
		stream.WriteByte(0x11);
		stream.WriteByte(0x70); // 70000 in big-endian
		stream.Write(payload, 0, payload.Length);
		stream.Position = 0;

		WebSocketFrameReader reader = new(stream);

		// Act
		WebSocketFrame frame = await reader.ReadFrameAsync(CancellationToken.None);

		// Assert
		Assert.Equal(WebSocketOpcode.Binary, frame.Opcode);
		Assert.Equal(70000, frame.Payload.Length);
	}

	[Fact]
	public async Task ReadFrameAsync_EmptyPayload_ParsesCorrectly()
	{
		// Arrange
		byte[] frameBytes = new byte[] { 0x81, 0x00 }; // Text with 0 bytes

		using MemoryStream stream = new(frameBytes);
		WebSocketFrameReader reader = new(stream);

		// Act
		WebSocketFrame frame = await reader.ReadFrameAsync(CancellationToken.None);

		// Assert
		Assert.Equal(WebSocketOpcode.Text, frame.Opcode);
		Assert.Empty(frame.Payload);
	}

	[Fact]
	public async Task ReadFrameAsync_UnexpectedEndOfStream_ThrowsEndOfStreamException()
	{
		// Arrange - Incomplete frame (header says 5 bytes but only 2 provided)
		byte[] frameBytes = new byte[] { 0x81, 0x05, 0x48, 0x65 };

		using MemoryStream stream = new(frameBytes);
		WebSocketFrameReader reader = new(stream);

		// Act & Assert
		await Assert.ThrowsAsync<EndOfStreamException>(() =>
			reader.ReadFrameAsync(CancellationToken.None));
	}

	[Fact]
	public async Task ReadFrameAsync_ContinuationFrame_ParsesCorrectly()
	{
		// Arrange
		byte[] frameBytes = new byte[] { 0x00, 0x03, 0x41, 0x42, 0x43 }; // Continuation, "ABC"

		using MemoryStream stream = new(frameBytes);
		WebSocketFrameReader reader = new(stream);

		// Act
		WebSocketFrame frame = await reader.ReadFrameAsync(CancellationToken.None);

		// Assert
		Assert.False(frame.IsFinal);
		Assert.Equal(WebSocketOpcode.Continuation, frame.Opcode);
		Assert.Equal("ABC", System.Text.Encoding.UTF8.GetString(frame.Payload));
	}

	#region Error Cases

	[Fact]
	public async Task ReadFrameAsync_EmptyStream_ThrowsEndOfStreamException()
	{
		// Arrange
		using MemoryStream stream = new(Array.Empty<byte>());
		WebSocketFrameReader reader = new(stream);

		// Act & Assert
		await Assert.ThrowsAsync<EndOfStreamException>(() =>
			reader.ReadFrameAsync(CancellationToken.None));
	}

	[Fact]
	public async Task ReadFrameAsync_OnlyOneByte_ThrowsEndOfStreamException()
	{
		// Arrange - Only first header byte
		byte[] frameBytes = new byte[] { 0x81 };

		using MemoryStream stream = new(frameBytes);
		WebSocketFrameReader reader = new(stream);

		// Act & Assert
		await Assert.ThrowsAsync<EndOfStreamException>(() =>
			reader.ReadFrameAsync(CancellationToken.None));
	}

	[Fact]
	public async Task ReadFrameAsync_MissingMaskingKey_ThrowsEndOfStreamException()
	{
		// Arrange - Masked frame but no masking key provided
		byte[] frameBytes = new byte[] { 0x81, 0x85 }; // MASK=1, Length=5 but no key

		using MemoryStream stream = new(frameBytes);
		WebSocketFrameReader reader = new(stream);

		// Act & Assert
		await Assert.ThrowsAsync<EndOfStreamException>(() =>
			reader.ReadFrameAsync(CancellationToken.None));
	}

	[Fact]
	public async Task ReadFrameAsync_PartialMaskingKey_ThrowsEndOfStreamException()
	{
		// Arrange - Masked frame with partial masking key
		byte[] frameBytes = new byte[] { 0x81, 0x85, 0x01, 0x02 }; // Only 2 bytes of mask key

		using MemoryStream stream = new(frameBytes);
		WebSocketFrameReader reader = new(stream);

		// Act & Assert
		await Assert.ThrowsAsync<EndOfStreamException>(() =>
			reader.ReadFrameAsync(CancellationToken.None));
	}

	[Fact]
	public async Task ReadFrameAsync_Extended16Bit_MissingLengthBytes_ThrowsEndOfStreamException()
	{
		// Arrange - Extended length indicator but no length bytes
		byte[] frameBytes = new byte[] { 0x81, 126 };

		using MemoryStream stream = new(frameBytes);
		WebSocketFrameReader reader = new(stream);

		// Act & Assert
		await Assert.ThrowsAsync<EndOfStreamException>(() =>
			reader.ReadFrameAsync(CancellationToken.None));
	}

	[Fact]
	public async Task ReadFrameAsync_Extended16Bit_PartialLengthBytes_ThrowsEndOfStreamException()
	{
		// Arrange - Extended length indicator with only 1 of 2 length bytes
		byte[] frameBytes = new byte[] { 0x81, 126, 0x00 };

		using MemoryStream stream = new(frameBytes);
		WebSocketFrameReader reader = new(stream);

		// Act & Assert
		await Assert.ThrowsAsync<EndOfStreamException>(() =>
			reader.ReadFrameAsync(CancellationToken.None));
	}

	[Fact]
	public async Task ReadFrameAsync_Extended64Bit_MissingLengthBytes_ThrowsEndOfStreamException()
	{
		// Arrange - 64-bit extended length indicator but no length bytes
		byte[] frameBytes = new byte[] { 0x81, 127 };

		using MemoryStream stream = new(frameBytes);
		WebSocketFrameReader reader = new(stream);

		// Act & Assert
		await Assert.ThrowsAsync<EndOfStreamException>(() =>
			reader.ReadFrameAsync(CancellationToken.None));
	}

	[Fact]
	public async Task ReadFrameAsync_Extended64Bit_PartialLengthBytes_ThrowsEndOfStreamException()
	{
		// Arrange - 64-bit extended length indicator with only 4 of 8 length bytes
		byte[] frameBytes = new byte[] { 0x81, 127, 0x00, 0x00, 0x00, 0x00 };

		using MemoryStream stream = new(frameBytes);
		WebSocketFrameReader reader = new(stream);

		// Act & Assert
		await Assert.ThrowsAsync<EndOfStreamException>(() =>
			reader.ReadFrameAsync(CancellationToken.None));
	}

	[Fact]
	public async Task ReadFrameAsync_PayloadTruncatedInMiddle_ThrowsEndOfStreamException()
	{
		// Arrange - Header says 10 bytes but only 5 bytes of payload provided
		byte[] frameBytes = new byte[] { 0x81, 0x0A, 0x01, 0x02, 0x03, 0x04, 0x05 };

		using MemoryStream stream = new(frameBytes);
		WebSocketFrameReader reader = new(stream);

		// Act & Assert
		await Assert.ThrowsAsync<EndOfStreamException>(() =>
			reader.ReadFrameAsync(CancellationToken.None));
	}

	[Fact]
	public async Task ReadFrameAsync_CancellationRequested_ThrowsCanceledException()
	{
		// Arrange
		using MemoryStream stream = new(new byte[] { 0x81, 0x05, 0x48, 0x65, 0x6C, 0x6C, 0x6F });
		WebSocketFrameReader reader = new(stream);

		using CancellationTokenSource cts = new();
		cts.Cancel(); // Cancel immediately

		// Act & Assert - TaskCanceledException is a subclass of OperationCanceledException
		await Assert.ThrowsAsync<TaskCanceledException>(() =>
			reader.ReadFrameAsync(cts.Token));
	}

	[Fact]
	public void Constructor_NullStream_ThrowsArgumentNullException()
	{
		// Act & Assert
		Assert.Throws<ArgumentNullException>(() => new WebSocketFrameReader(null!));
	}

	[Fact]
	public async Task ReadFrameAsync_MaskedFrameWithIncompletePayload_ThrowsEndOfStreamException()
	{
		// Arrange - Masked frame with complete key but incomplete payload
		byte[] frameBytes = new byte[] { 0x81, 0x85, 0x01, 0x02, 0x03, 0x04, 0x01, 0x02 }; // Only 2 of 5 payload bytes

		using MemoryStream stream = new(frameBytes);
		WebSocketFrameReader reader = new(stream);

		// Act & Assert
		await Assert.ThrowsAsync<EndOfStreamException>(() =>
			reader.ReadFrameAsync(CancellationToken.None));
	}

	[Theory]
	[InlineData(0x03)] // Reserved opcode
	[InlineData(0x04)] // Reserved opcode
	[InlineData(0x05)] // Reserved opcode
	[InlineData(0x06)] // Reserved opcode
	[InlineData(0x07)] // Reserved opcode
	[InlineData(0x0B)] // Reserved opcode
	[InlineData(0x0C)] // Reserved opcode
	[InlineData(0x0D)] // Reserved opcode
	[InlineData(0x0E)] // Reserved opcode
	[InlineData(0x0F)] // Reserved opcode
	public async Task ReadFrameAsync_ReservedOpcode_ParsesWithoutError(int reservedOpcode)
	{
		// Arrange - Frame with reserved opcode
		byte[] frameBytes = new byte[] { (byte)(0x80 | reservedOpcode), 0x00 };

		using MemoryStream stream = new(frameBytes);
		WebSocketFrameReader reader = new(stream);

		// Act
		WebSocketFrame frame = await reader.ReadFrameAsync(CancellationToken.None);

		// Assert - Frame reader should parse it, handling is up to higher layer
		Assert.Equal((WebSocketOpcode)reservedOpcode, frame.Opcode);
	}

	#endregion
}
