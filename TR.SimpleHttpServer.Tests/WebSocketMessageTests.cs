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
}
