using System;
using System.Text;
using Xunit;
using TR.SimpleHttpServer.WebSocket;

namespace TR.SimpleHttpServer.Tests;

public class WebSocketMessageTests
{
	[Fact]
	public void Constructor_ValidTextMessage_PropertiesSetCorrectly()
	{
		// Arrange
		byte[] data = Encoding.UTF8.GetBytes("Hello");

		// Act
		WebSocketMessage message = new(WebSocketMessageType.Text, data);

		// Assert
		Assert.Equal(WebSocketMessageType.Text, message.Type);
		Assert.Equal(data, message.Data);
		Assert.Null(message.CloseStatus);
		Assert.Equal("", message.CloseReason);
	}

	[Fact]
	public void Constructor_CloseMessage_PropertiesSetCorrectly()
	{
		// Arrange
		byte[] data = Array.Empty<byte>();

		// Act
		WebSocketMessage message = new(WebSocketMessageType.Close, data, WebSocketCloseStatus.NormalClosure, "Goodbye");

		// Assert
		Assert.Equal(WebSocketMessageType.Close, message.Type);
		Assert.Equal(WebSocketCloseStatus.NormalClosure, message.CloseStatus);
		Assert.Equal("Goodbye", message.CloseReason);
	}

	[Fact]
	public void Constructor_NullData_ThrowsArgumentNullException()
	{
		// Act & Assert
		Assert.Throws<ArgumentNullException>(() => new WebSocketMessage(WebSocketMessageType.Text, null!));
	}

	[Fact]
	public void Constructor_NullCloseReason_SetsEmptyString()
	{
		// Arrange
		byte[] data = Array.Empty<byte>();

		// Act
		WebSocketMessage message = new(WebSocketMessageType.Close, data, WebSocketCloseStatus.NormalClosure, null!);

		// Assert
		Assert.Equal("", message.CloseReason);
	}

	[Fact]
	public void GetText_ValidUtf8Data_ReturnsCorrectString()
	{
		// Arrange
		string expectedText = "Hello, World!";
		byte[] data = Encoding.UTF8.GetBytes(expectedText);
		WebSocketMessage message = new(WebSocketMessageType.Text, data);

		// Act
		string actualText = message.GetText();

		// Assert
		Assert.Equal(expectedText, actualText);
	}

	[Fact]
	public void GetText_EmptyData_ReturnsEmptyString()
	{
		// Arrange
		WebSocketMessage message = new(WebSocketMessageType.Text, Array.Empty<byte>());

		// Act
		string text = message.GetText();

		// Assert
		Assert.Equal("", text);
	}

	[Fact]
	public void GetText_BinaryMessage_ReturnsDecodedString()
	{
		// Arrange - Even binary messages can be decoded as text
		byte[] data = Encoding.UTF8.GetBytes("Binary content");
		WebSocketMessage message = new(WebSocketMessageType.Binary, data);

		// Act
		string text = message.GetText();

		// Assert
		Assert.Equal("Binary content", text);
	}

	[Fact]
	public void GetText_JapaneseCharacters_ReturnsCorrectString()
	{
		// Arrange
		string expectedText = "こんにちは世界";
		byte[] data = Encoding.UTF8.GetBytes(expectedText);
		WebSocketMessage message = new(WebSocketMessageType.Text, data);

		// Act
		string actualText = message.GetText();

		// Assert
		Assert.Equal(expectedText, actualText);
	}

	[Fact]
	public void MessageType_Text_IsCorrect()
	{
		// Arrange & Act
		WebSocketMessage message = new(WebSocketMessageType.Text, new byte[] { 0x41 });

		// Assert
		Assert.Equal(WebSocketMessageType.Text, message.Type);
	}

	[Fact]
	public void MessageType_Binary_IsCorrect()
	{
		// Arrange & Act
		WebSocketMessage message = new(WebSocketMessageType.Binary, new byte[] { 0x41 });

		// Assert
		Assert.Equal(WebSocketMessageType.Binary, message.Type);
	}

	[Fact]
	public void MessageType_Close_IsCorrect()
	{
		// Arrange & Act
		WebSocketMessage message = new(WebSocketMessageType.Close, Array.Empty<byte>());

		// Assert
		Assert.Equal(WebSocketMessageType.Close, message.Type);
	}

	#region Error Cases

	[Fact]
	public void GetText_InvalidUtf8Bytes_HandlesGracefully()
	{
		// Arrange - Invalid UTF-8 sequence
		byte[] invalidUtf8 = new byte[] { 0xFF, 0xFE, 0x00, 0x01 };
		WebSocketMessage message = new(WebSocketMessageType.Text, invalidUtf8);

		// Act - Should not throw, but may produce replacement characters
		string result = message.GetText();

		// Assert - The method should complete without throwing
		Assert.NotNull(result);
	}

	[Fact]
	public void Constructor_CloseMessageWithNullStatus_SetsNullCloseStatus()
	{
		// Arrange
		byte[] data = Array.Empty<byte>();

		// Act
		WebSocketMessage message = new(WebSocketMessageType.Close, data, null, "");

		// Assert
		Assert.Null(message.CloseStatus);
	}

	[Fact]
	public void Constructor_TextWithCloseStatus_AllowsButUnusual()
	{
		// Arrange - Unusual but allowed: text message with close status
		byte[] data = Encoding.UTF8.GetBytes("Hello");

		// Act
		WebSocketMessage message = new(WebSocketMessageType.Text, data, WebSocketCloseStatus.NormalClosure, "reason");

		// Assert - Properties are set as provided
		Assert.Equal(WebSocketMessageType.Text, message.Type);
		Assert.Equal(WebSocketCloseStatus.NormalClosure, message.CloseStatus);
		Assert.Equal("reason", message.CloseReason);
	}

	[Fact]
	public void GetText_LargeData_ReturnsCorrectString()
	{
		// Arrange - Large text
		string largeText = new string('A', 100000);
		byte[] data = Encoding.UTF8.GetBytes(largeText);
		WebSocketMessage message = new(WebSocketMessageType.Text, data);

		// Act
		string result = message.GetText();

		// Assert
		Assert.Equal(largeText, result);
	}

	[Fact]
	public void GetText_EmojiCharacters_ReturnsCorrectString()
	{
		// Arrange - Text with emoji (4-byte UTF-8)
		string emojiText = "Hello 👋 World 🌍";
		byte[] data = Encoding.UTF8.GetBytes(emojiText);
		WebSocketMessage message = new(WebSocketMessageType.Text, data);

		// Act
		string result = message.GetText();

		// Assert
		Assert.Equal(emojiText, result);
	}

	[Fact]
	public void Constructor_AllCloseStatusValues_Accepted()
	{
		// Arrange & Act & Assert
		foreach (WebSocketCloseStatus status in Enum.GetValues(typeof(WebSocketCloseStatus)))
		{
			WebSocketMessage message = new(WebSocketMessageType.Close, Array.Empty<byte>(), status, "");
			Assert.Equal(status, message.CloseStatus);
		}
	}

	[Fact]
	public void CloseReason_LongReason_Accepted()
	{
		// Arrange - Close reason can be any length in WebSocketMessage (125 byte limit is at frame level)
		string longReason = new string('x', 1000);
		byte[] data = Array.Empty<byte>();

		// Act
		WebSocketMessage message = new(WebSocketMessageType.Close, data, WebSocketCloseStatus.NormalClosure, longReason);

		// Assert
		Assert.Equal(longReason, message.CloseReason);
	}

	[Fact]
	public void Data_IsSameReferenceAsProvided()
	{
		// Arrange
		byte[] data = new byte[] { 0x01, 0x02, 0x03 };

		// Act
		WebSocketMessage message = new(WebSocketMessageType.Binary, data);

		// Assert - Data should be the same reference
		Assert.Same(data, message.Data);
	}

	#endregion
}
