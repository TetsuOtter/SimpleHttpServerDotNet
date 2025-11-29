using System;
using Xunit;
using TR.SimpleHttpServer.WebSocket;

namespace TR.SimpleHttpServer.Tests;

public class WebSocketFrameTests
{
	[Fact]
	public void ToBytes_UnmaskedTextFrame_ProducesCorrectOutput()
	{
		// Arrange
		byte[] payload = System.Text.Encoding.UTF8.GetBytes("Hello");
		WebSocketFrame frame = new(true, WebSocketOpcode.Text, payload);

		// Act
		byte[] result = frame.ToBytes();

		// Assert
		Assert.Equal(0x81, result[0]); // FIN=1, Opcode=1 (text)
		Assert.Equal(5, result[1]); // Length=5, no mask
		Assert.Equal(payload, result[2..]);
	}

	[Fact]
	public void ToBytes_UnmaskedBinaryFrame_ProducesCorrectOutput()
	{
		// Arrange
		byte[] payload = new byte[] { 0x01, 0x02, 0x03 };
		WebSocketFrame frame = new(true, WebSocketOpcode.Binary, payload);

		// Act
		byte[] result = frame.ToBytes();

		// Assert
		Assert.Equal(0x82, result[0]); // FIN=1, Opcode=2 (binary)
		Assert.Equal(3, result[1]); // Length=3, no mask
		Assert.Equal(payload, result[2..]);
	}

	[Fact]
	public void ToBytes_MediumPayload_Uses16BitLength()
	{
		// Arrange
		byte[] payload = new byte[200]; // > 125, < 65536
		WebSocketFrame frame = new(true, WebSocketOpcode.Text, payload);

		// Act
		byte[] result = frame.ToBytes();

		// Assert
		Assert.Equal(0x81, result[0]); // FIN=1, Opcode=1 (text)
		Assert.Equal(126, result[1]); // Extended length indicator
		Assert.Equal(0, result[2]); // Length high byte
		Assert.Equal(200, result[3]); // Length low byte
		Assert.Equal(payload.Length, result.Length - 4);
	}

	[Fact]
	public void ToBytes_LargePayload_Uses64BitLength()
	{
		// Arrange
		byte[] payload = new byte[70000]; // > 65535
		WebSocketFrame frame = new(true, WebSocketOpcode.Text, payload);

		// Act
		byte[] result = frame.ToBytes();

		// Assert
		Assert.Equal(0x81, result[0]); // FIN=1, Opcode=1 (text)
		Assert.Equal(127, result[1]); // 64-bit extended length indicator
																	// Length is in next 8 bytes (big-endian)
		Assert.Equal(payload.Length, result.Length - 10);
	}

	[Fact]
	public void ToBytes_MaskedFrame_AppliesMasking()
	{
		// Arrange
		byte[] payload = new byte[] { 0x01, 0x02, 0x03, 0x04 };
		byte[] maskingKey = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
		WebSocketFrame frame = new(true, WebSocketOpcode.Text, true, maskingKey, payload);

		// Act
		byte[] result = frame.ToBytes();

		// Assert
		Assert.Equal(0x81, result[0]); // FIN=1, Opcode=1 (text)
		Assert.Equal(0x84, result[1]); // MASK=1, Length=4
		Assert.Equal(maskingKey, result[2..6]); // Masking key
		Assert.Equal(new byte[] { 0xFE, 0xFD, 0xFC, 0xFB }, result[6..10]); // XOR with FF
	}

	[Fact]
	public void ToBytes_NonFinalFrame_SetsFinBitToZero()
	{
		// Arrange
		byte[] payload = new byte[] { 0x01 };
		WebSocketFrame frame = new(false, WebSocketOpcode.Text, payload);

		// Act
		byte[] result = frame.ToBytes();

		// Assert
		Assert.Equal(0x01, result[0]); // FIN=0, Opcode=1 (text)
	}

	[Fact]
	public void ToBytes_PingFrame_ProducesCorrectOutput()
	{
		// Arrange
		byte[] payload = Array.Empty<byte>();
		WebSocketFrame frame = new(true, WebSocketOpcode.Ping, payload);

		// Act
		byte[] result = frame.ToBytes();

		// Assert
		Assert.Equal(0x89, result[0]); // FIN=1, Opcode=9 (ping)
		Assert.Equal(0, result[1]); // Length=0
	}

	[Fact]
	public void ToBytes_PongFrame_ProducesCorrectOutput()
	{
		// Arrange
		byte[] payload = new byte[] { 0x01 };
		WebSocketFrame frame = new(true, WebSocketOpcode.Pong, payload);

		// Act
		byte[] result = frame.ToBytes();

		// Assert
		Assert.Equal(0x8A, result[0]); // FIN=1, Opcode=10 (pong)
		Assert.Equal(1, result[1]); // Length=1
	}

	[Fact]
	public void ToBytes_CloseFrame_ProducesCorrectOutput()
	{
		// Arrange
		byte[] payload = new byte[] { 0x03, 0xE8 }; // Close code 1000
		WebSocketFrame frame = new(true, WebSocketOpcode.Close, payload);

		// Act
		byte[] result = frame.ToBytes();

		// Assert
		Assert.Equal(0x88, result[0]); // FIN=1, Opcode=8 (close)
		Assert.Equal(2, result[1]); // Length=2
		Assert.Equal(payload, result[2..4]);
	}

	[Fact]
	public void ApplyMask_ValidData_AppliesMaskCorrectly()
	{
		// Arrange
		byte[] data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
		byte[] maskingKey = new byte[] { 0xFF, 0x00, 0xFF, 0x00 };

		// Act
		byte[] result = WebSocketFrame.ApplyMask(data, maskingKey);

		// Assert
		Assert.Equal(new byte[] { 0xFE, 0x02, 0xFC, 0x04, 0xFA }, result);
	}

	[Fact]
	public void ApplyMask_DoubleApply_ReturnsOriginal()
	{
		// Arrange
		byte[] originalData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
		byte[] maskingKey = new byte[] { 0xAB, 0xCD, 0xEF, 0x12 };

		// Act
		byte[] masked = WebSocketFrame.ApplyMask(originalData, maskingKey);
		byte[] unmasked = WebSocketFrame.ApplyMask(masked, maskingKey);

		// Assert
		Assert.Equal(originalData, unmasked);
	}

	[Fact]
	public void Constructor_MaskedWithInvalidKeyLength_ThrowsArgumentException()
	{
		// Arrange
		byte[] payload = new byte[] { 0x01 };
		byte[] invalidKey = new byte[] { 0x01, 0x02 }; // Only 2 bytes

		// Act & Assert
		Assert.Throws<ArgumentException>(() => new WebSocketFrame(true, WebSocketOpcode.Text, true, invalidKey, payload));
	}

	[Fact]
	public void ApplyMask_InvalidKeyLength_ThrowsArgumentException()
	{
		// Arrange
		byte[] data = new byte[] { 0x01 };
		byte[] invalidKey = new byte[] { 0x01, 0x02 }; // Only 2 bytes

		// Act & Assert
		Assert.Throws<ArgumentException>(() => WebSocketFrame.ApplyMask(data, invalidKey));
	}

	#region Error Cases

	[Fact]
	public void Constructor_MaskedWithEmptyKey_ThrowsArgumentException()
	{
		// Arrange
		byte[] payload = new byte[] { 0x01 };
		byte[] emptyKey = Array.Empty<byte>();

		// Act & Assert
		Assert.Throws<ArgumentException>(() => new WebSocketFrame(true, WebSocketOpcode.Text, true, emptyKey, payload));
	}

	[Fact]
	public void Constructor_MaskedWithTooLongKey_ThrowsArgumentException()
	{
		// Arrange
		byte[] payload = new byte[] { 0x01 };
		byte[] longKey = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 }; // 5 bytes

		// Act & Assert
		Assert.Throws<ArgumentException>(() => new WebSocketFrame(true, WebSocketOpcode.Text, true, longKey, payload));
	}

	[Fact]
	public void ApplyMask_EmptyKey_ThrowsArgumentException()
	{
		// Arrange
		byte[] data = new byte[] { 0x01 };
		byte[] emptyKey = Array.Empty<byte>();

		// Act & Assert
		Assert.Throws<ArgumentException>(() => WebSocketFrame.ApplyMask(data, emptyKey));
	}

	[Fact]
	public void ApplyMask_TooLongKey_ThrowsArgumentException()
	{
		// Arrange
		byte[] data = new byte[] { 0x01 };
		byte[] longKey = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 }; // 5 bytes

		// Act & Assert
		Assert.Throws<ArgumentException>(() => WebSocketFrame.ApplyMask(data, longKey));
	}

	[Fact]
	public void ApplyMask_EmptyData_ReturnsEmptyArray()
	{
		// Arrange
		byte[] data = Array.Empty<byte>();
		byte[] maskingKey = new byte[] { 0x01, 0x02, 0x03, 0x04 };

		// Act
		byte[] result = WebSocketFrame.ApplyMask(data, maskingKey);

		// Assert
		Assert.Empty(result);
	}

	[Theory]
	[InlineData(WebSocketOpcode.Text)]
	[InlineData(WebSocketOpcode.Binary)]
	[InlineData(WebSocketOpcode.Close)]
	[InlineData(WebSocketOpcode.Ping)]
	[InlineData(WebSocketOpcode.Pong)]
	[InlineData(WebSocketOpcode.Continuation)]
	public void ToBytes_AllOpcodes_ProducesValidFirstByte(WebSocketOpcode opcode)
	{
		// Arrange
		byte[] payload = new byte[] { 0x01 };
		WebSocketFrame frame = new(true, opcode, payload);

		// Act
		byte[] result = frame.ToBytes();

		// Assert
		int expectedOpcodeBits = (int)opcode & 0x0F;
		Assert.Equal(0x80 | expectedOpcodeBits, result[0]); // FIN=1 + opcode
	}

	[Fact]
	public void ToBytes_ExactlyLength125_DoesNotUseExtendedLength()
	{
		// Arrange
		byte[] payload = new byte[125];
		WebSocketFrame frame = new(true, WebSocketOpcode.Binary, payload);

		// Act
		byte[] result = frame.ToBytes();

		// Assert
		Assert.Equal(125, result[1]); // Length=125, no extended length
		Assert.Equal(127, result.Length); // 2 header + 125 payload
	}

	[Fact]
	public void ToBytes_ExactlyLength126_Uses16BitExtendedLength()
	{
		// Arrange
		byte[] payload = new byte[126];
		WebSocketFrame frame = new(true, WebSocketOpcode.Binary, payload);

		// Act
		byte[] result = frame.ToBytes();

		// Assert
		Assert.Equal(126, result[1]); // Extended length indicator
		Assert.Equal(0, result[2]); // High byte
		Assert.Equal(126, result[3]); // Low byte
		Assert.Equal(130, result.Length); // 2 + 2 + 126 = 130
	}

	[Fact]
	public void ToBytes_ExactlyLength65535_Uses16BitExtendedLength()
	{
		// Arrange
		byte[] payload = new byte[65535];
		WebSocketFrame frame = new(true, WebSocketOpcode.Binary, payload);

		// Act
		byte[] result = frame.ToBytes();

		// Assert
		Assert.Equal(126, result[1]); // Extended length indicator
		Assert.Equal(0xFF, result[2]); // High byte
		Assert.Equal(0xFF, result[3]); // Low byte
	}

	[Fact]
	public void ToBytes_ExactlyLength65536_Uses64BitExtendedLength()
	{
		// Arrange
		byte[] payload = new byte[65536];
		WebSocketFrame frame = new(true, WebSocketOpcode.Binary, payload);

		// Act
		byte[] result = frame.ToBytes();

		// Assert
		Assert.Equal(127, result[1]); // 64-bit extended length indicator
																	// Verify frame size: 2 header + 8 length + 65536 payload
		Assert.Equal(65546, result.Length);
		// Verify payload is intact
		Assert.Equal(65536, result.Length - 10); // 10 = 2 header bytes + 8 length bytes
	}
	[Fact]
	public void ToBytes_EmptyPayload_ProducesValidFrame()
	{
		// Arrange
		byte[] payload = Array.Empty<byte>();
		WebSocketFrame frame = new(true, WebSocketOpcode.Text, payload);

		// Act
		byte[] result = frame.ToBytes();

		// Assert
		Assert.Equal(2, result.Length); // Minimum frame size
		Assert.Equal(0x81, result[0]); // FIN=1, Text
		Assert.Equal(0x00, result[1]); // Length=0
	}

	#endregion
}
