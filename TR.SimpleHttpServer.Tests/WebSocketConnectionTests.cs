using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using TR.SimpleHttpServer.WebSocket;

namespace TR.SimpleHttpServer.Tests;

public class WebSocketConnectionTests : IDisposable
{
	private readonly MemoryStream _stream;
	private readonly WebSocketConnection _connection;

	public WebSocketConnectionTests()
	{
		_stream = new MemoryStream();
		_connection = new WebSocketConnection(_stream);
	}

	public void Dispose()
	{
		_connection.Dispose();
		_stream.Dispose();
	}

	#region Constructor Tests

	[Fact]
	public void Constructor_NullStream_ThrowsArgumentNullException()
	{
		// Act & Assert
		Assert.Throws<ArgumentNullException>(() => new WebSocketConnection(null!));
	}

	[Fact]
	public void Constructor_ValidStream_IsOpenTrue()
	{
		// Assert
		Assert.True(_connection.IsOpen);
	}

	#endregion

	#region Dispose Tests

	[Fact]
	public void Dispose_SetsIsOpenFalse()
	{
		// Act
		_connection.Dispose();

		// Assert
		Assert.False(_connection.IsOpen);
	}

	[Fact]
	public void Dispose_MultipleCalls_DoesNotThrow()
	{
		// Act & Assert - Multiple dispose calls should not throw
		_connection.Dispose();
		_connection.Dispose();
		_connection.Dispose();
	}

	[Fact]
	public async Task SendTextAsync_AfterDispose_ThrowsObjectDisposedException()
	{
		// Arrange
		_connection.Dispose();

		// Act & Assert
		await Assert.ThrowsAsync<ObjectDisposedException>(() =>
			_connection.SendTextAsync("test", CancellationToken.None));
	}

	[Fact]
	public async Task SendBinaryAsync_AfterDispose_ThrowsObjectDisposedException()
	{
		// Arrange
		_connection.Dispose();

		// Act & Assert
		await Assert.ThrowsAsync<ObjectDisposedException>(() =>
			_connection.SendBinaryAsync(new byte[] { 0x01 }, CancellationToken.None));
	}

	[Fact]
	public async Task SendPingAsync_AfterDispose_ThrowsObjectDisposedException()
	{
		// Arrange
		_connection.Dispose();

		// Act & Assert
		await Assert.ThrowsAsync<ObjectDisposedException>(() =>
			_connection.SendPingAsync(new byte[] { 0x01 }, CancellationToken.None));
	}

	[Fact]
	public async Task CloseAsync_AfterDispose_ThrowsObjectDisposedException()
	{
		// Arrange
		_connection.Dispose();

		// Act & Assert
		await Assert.ThrowsAsync<ObjectDisposedException>(() =>
			_connection.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None));
	}

	[Fact]
	public async Task ReceiveMessageAsync_AfterDispose_ThrowsObjectDisposedException()
	{
		// Arrange
		_connection.Dispose();

		// Act & Assert
		await Assert.ThrowsAsync<ObjectDisposedException>(() =>
			_connection.ReceiveMessageAsync(CancellationToken.None));
	}

	#endregion

	#region ReceiveMessageAsync Error Cases

	[Fact]
	public async Task ReceiveMessageAsync_EmptyStream_ThrowsEndOfStreamException()
	{
		// Arrange - Empty stream
		_stream.Position = 0;

		// Act & Assert
		await Assert.ThrowsAsync<EndOfStreamException>(() =>
			_connection.ReceiveMessageAsync(CancellationToken.None));
	}

	[Fact]
	public async Task ReceiveMessageAsync_ConnectionClosed_ThrowsInvalidOperationException()
	{
		// Arrange - Write a close frame to simulate closed connection
		byte[] closeFrame = new byte[] { 0x88, 0x02, 0x03, 0xE8 }; // Close with status 1000
		_stream.Write(closeFrame, 0, closeFrame.Length);
		_stream.Position = 0;

		// Act - First receive gets the close message
		var message = await _connection.ReceiveMessageAsync(CancellationToken.None);
		Assert.Equal(WebSocketMessageType.Close, message.Type);

		// Assert - Connection is now closed, second receive throws
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			_connection.ReceiveMessageAsync(CancellationToken.None));
	}

	[Fact]
	public async Task ReceiveMessageAsync_CancellationRequested_ThrowsCanceledException()
	{
		// Arrange
		byte[] textFrame = new byte[] { 0x81, 0x05, 0x48, 0x65, 0x6C, 0x6C, 0x6F };
		_stream.Write(textFrame, 0, textFrame.Length);
		_stream.Position = 0;

		using var cts = new CancellationTokenSource();
		cts.Cancel();

		// Act & Assert - TaskCanceledException is a subclass of OperationCanceledException
		// Both are caught by catching OperationCanceledException
		await Assert.ThrowsAsync<TaskCanceledException>(() =>
			_connection.ReceiveMessageAsync(cts.Token));
	}

	[Fact]
	public async Task ReceiveMessageAsync_ContinuationWithoutInitialFrame_ThrowsInvalidOperationException()
	{
		// Arrange - Continuation frame without preceding data frame
		byte[] continuationFrame = new byte[] { 0x80, 0x03, 0x41, 0x42, 0x43 }; // FIN=1, Continuation, "ABC"
		_stream.Write(continuationFrame, 0, continuationFrame.Length);
		_stream.Position = 0;

		// Act & Assert
		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			_connection.ReceiveMessageAsync(CancellationToken.None));
	}

	#endregion

	#region SendTextAsync Tests

	[Fact]
	public async Task SendTextAsync_ValidMessage_WritesFrameToStream()
	{
		// Arrange
		string message = "Hello";

		// Act
		await _connection.SendTextAsync(message, CancellationToken.None);

		// Assert
		_stream.Position = 0;
		byte[] result = _stream.ToArray();
		Assert.Equal(0x81, result[0]); // FIN=1, Text
		Assert.Equal(5, result[1]); // Length=5
		Assert.Equal("Hello", Encoding.UTF8.GetString(result, 2, 5));
	}

	[Fact]
	public async Task SendTextAsync_EmptyMessage_WritesEmptyFrame()
	{
		// Arrange
		string message = "";

		// Act
		await _connection.SendTextAsync(message, CancellationToken.None);

		// Assert
		_stream.Position = 0;
		byte[] result = _stream.ToArray();
		Assert.Equal(0x81, result[0]); // FIN=1, Text
		Assert.Equal(0, result[1]); // Length=0
		Assert.Equal(2, result.Length);
	}

	[Fact]
	public async Task SendTextAsync_JapaneseText_WritesCorrectUtf8()
	{
		// Arrange
		string message = "こんにちは";

		// Act
		await _connection.SendTextAsync(message, CancellationToken.None);

		// Assert
		_stream.Position = 0;
		byte[] result = _stream.ToArray();
		Assert.Equal(0x81, result[0]); // FIN=1, Text
		byte[] expectedPayload = Encoding.UTF8.GetBytes(message);
		Assert.Equal(expectedPayload.Length, result[1]);
	}

	#endregion

	#region SendBinaryAsync Tests

	[Fact]
	public async Task SendBinaryAsync_ValidData_WritesFrameToStream()
	{
		// Arrange
		byte[] data = new byte[] { 0x01, 0x02, 0x03 };

		// Act
		await _connection.SendBinaryAsync(data, CancellationToken.None);

		// Assert
		_stream.Position = 0;
		byte[] result = _stream.ToArray();
		Assert.Equal(0x82, result[0]); // FIN=1, Binary
		Assert.Equal(3, result[1]); // Length=3
		Assert.Equal(data, result[2..5]);
	}

	[Fact]
	public async Task SendBinaryAsync_EmptyData_WritesEmptyFrame()
	{
		// Arrange
		byte[] data = Array.Empty<byte>();

		// Act
		await _connection.SendBinaryAsync(data, CancellationToken.None);

		// Assert
		_stream.Position = 0;
		byte[] result = _stream.ToArray();
		Assert.Equal(0x82, result[0]); // FIN=1, Binary
		Assert.Equal(0, result[1]); // Length=0
		Assert.Equal(2, result.Length);
	}

	#endregion

	#region CloseAsync Tests

	[Fact]
	public async Task CloseAsync_ValidStatusAndReason_WritesCloseFrame()
	{
		// Arrange
		var status = WebSocketCloseStatus.NormalClosure;
		string reason = "Goodbye";

		// Act
		await _connection.CloseAsync(status, reason, CancellationToken.None);

		// Assert
		_stream.Position = 0;
		byte[] result = _stream.ToArray();
		Assert.Equal(0x88, result[0]); // FIN=1, Close
																	 // Payload should contain status code (2 bytes) + reason
		Assert.Equal(2 + reason.Length, result[1]);
		// Status code 1000 = 0x03E8
		Assert.Equal(0x03, result[2]);
		Assert.Equal(0xE8, result[3]);
		Assert.Equal(reason, Encoding.UTF8.GetString(result, 4, reason.Length));
	}

	[Fact]
	public async Task CloseAsync_EmptyReason_WritesCloseFrameWithStatusOnly()
	{
		// Arrange
		var status = WebSocketCloseStatus.NormalClosure;

		// Act
		await _connection.CloseAsync(status, "", CancellationToken.None);

		// Assert
		_stream.Position = 0;
		byte[] result = _stream.ToArray();
		Assert.Equal(0x88, result[0]); // FIN=1, Close
		Assert.Equal(2, result[1]); // Just status code
	}

	[Fact]
	public async Task CloseAsync_AlreadyClosed_DoesNotSendAgain()
	{
		// Arrange
		var status = WebSocketCloseStatus.NormalClosure;

		// Act
		await _connection.CloseAsync(status, "", CancellationToken.None);
		long positionAfterFirstClose = _stream.Position;

		await _connection.CloseAsync(status, "", CancellationToken.None);
		long positionAfterSecondClose = _stream.Position;

		// Assert - Position should not change after second close
		Assert.Equal(positionAfterFirstClose, positionAfterSecondClose);
	}

	[Fact]
	public async Task CloseAsync_SetsIsOpenFalse()
	{
		// Act
		await _connection.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);

		// Assert
		Assert.False(_connection.IsOpen);
	}

	[Theory]
	[InlineData(WebSocketCloseStatus.NormalClosure)]
	[InlineData(WebSocketCloseStatus.GoingAway)]
	[InlineData(WebSocketCloseStatus.ProtocolError)]
	[InlineData(WebSocketCloseStatus.UnsupportedData)]
	[InlineData(WebSocketCloseStatus.InvalidPayloadData)]
	[InlineData(WebSocketCloseStatus.PolicyViolation)]
	[InlineData(WebSocketCloseStatus.MessageTooBig)]
	[InlineData(WebSocketCloseStatus.MandatoryExtension)]
	[InlineData(WebSocketCloseStatus.InternalServerError)]
	public async Task CloseAsync_AllStatusCodes_WritesCorrectStatusCode(WebSocketCloseStatus status)
	{
		// Act
		await _connection.CloseAsync(status, "", CancellationToken.None);

		// Assert
		_stream.Position = 0;
		byte[] result = _stream.ToArray();
		int writtenStatus = (result[2] << 8) | result[3];
		Assert.Equal((int)status, writtenStatus);
	}

	#endregion

	#region Ping/Pong Event Tests

	[Fact]
	public async Task ReceiveMessageAsync_PingFrame_RaisesPingReceivedEvent()
	{
		// Arrange
		byte[] pingPayload = new byte[] { 0x01, 0x02, 0x03 };
		byte[] pingFrame = new byte[] { 0x89, 0x03, 0x01, 0x02, 0x03 }; // Ping with payload

		// Write only the ping frame - the auto-response pong will be written to same stream
		_stream.Write(pingFrame, 0, pingFrame.Length);

		// We need to set up the stream to allow both read and write
		long initialPosition = _stream.Position;
		_stream.Position = 0;

		byte[]? receivedPayload = null;
		_connection.PingReceived += (sender, args) => receivedPayload = args.Payload;

		// Act - This should read the ping, trigger the event, and auto-respond with pong
		// Since there's no data frame after ping, it will throw EndOfStreamException
		// which is expected behavior - we catch it and verify the event was raised
		try
		{
			await _connection.ReceiveMessageAsync(CancellationToken.None);
		}
		catch (EndOfStreamException)
		{
			// Expected - no more frames after ping
		}

		// Assert - Event should have fired with correct payload
		Assert.NotNull(receivedPayload);
		Assert.Equal(pingPayload, receivedPayload);
	}

	[Fact]
	public async Task ReceiveMessageAsync_PongFrame_RaisesPongReceivedEvent()
	{
		// Arrange
		byte[] pongPayload = new byte[] { 0x04, 0x05, 0x06 };
		byte[] pongFrame = new byte[] { 0x8A, 0x03, 0x04, 0x05, 0x06 }; // Pong with payload

		_stream.Write(pongFrame, 0, pongFrame.Length);
		_stream.Position = 0;

		byte[]? receivedPayload = null;
		_connection.PongReceived += (sender, args) => receivedPayload = args.Payload;

		// Act - This should read the pong, trigger the event
		// Since there's no data frame after pong, it will throw EndOfStreamException
		// which is expected behavior - we catch it and verify the event was raised
		try
		{
			await _connection.ReceiveMessageAsync(CancellationToken.None);
		}
		catch (EndOfStreamException)
		{
			// Expected - no more frames after pong
		}

		// Assert - Event should have fired with correct payload
		Assert.NotNull(receivedPayload);
		Assert.Equal(pongPayload, receivedPayload);
	}

	#endregion

	#region Fragmented Message Tests

	[Fact]
	public async Task ReceiveMessageAsync_FragmentedMessage_AssemblesCorrectly()
	{
		// Arrange - Non-final text frame followed by final continuation frame
		byte[] firstFrame = new byte[] { 0x01, 0x03, 0x48, 0x65, 0x6C }; // FIN=0, Text, "Hel"
		byte[] secondFrame = new byte[] { 0x80, 0x02, 0x6C, 0x6F }; // FIN=1, Continuation, "lo"

		_stream.Write(firstFrame, 0, firstFrame.Length);
		_stream.Write(secondFrame, 0, secondFrame.Length);
		_stream.Position = 0;

		// Act
		var message = await _connection.ReceiveMessageAsync(CancellationToken.None);

		// Assert
		Assert.Equal(WebSocketMessageType.Text, message.Type);
		Assert.Equal("Hello", message.GetText());
	}

	[Fact]
	public async Task ReceiveMessageAsync_MultipleFragments_AssemblesCorrectly()
	{
		// Arrange - Three fragments
		byte[] firstFrame = new byte[] { 0x01, 0x01, 0x41 }; // FIN=0, Text, "A"
		byte[] secondFrame = new byte[] { 0x00, 0x01, 0x42 }; // FIN=0, Continuation, "B"
		byte[] thirdFrame = new byte[] { 0x80, 0x01, 0x43 }; // FIN=1, Continuation, "C"

		_stream.Write(firstFrame, 0, firstFrame.Length);
		_stream.Write(secondFrame, 0, secondFrame.Length);
		_stream.Write(thirdFrame, 0, thirdFrame.Length);
		_stream.Position = 0;

		// Act
		var message = await _connection.ReceiveMessageAsync(CancellationToken.None);

		// Assert
		Assert.Equal(WebSocketMessageType.Text, message.Type);
		Assert.Equal("ABC", message.GetText());
	}

	#endregion

	#region Close Frame Handling Tests

	[Fact]
	public async Task ReceiveMessageAsync_CloseFrameWithStatus_ReturnsCloseMessage()
	{
		// Arrange - Close frame with status 1001 (Going Away)
		byte[] closeFrame = new byte[] { 0x88, 0x02, 0x03, 0xE9 };
		_stream.Write(closeFrame, 0, closeFrame.Length);
		_stream.Position = 0;

		// Act
		var message = await _connection.ReceiveMessageAsync(CancellationToken.None);

		// Assert
		Assert.Equal(WebSocketMessageType.Close, message.Type);
		Assert.Equal(WebSocketCloseStatus.GoingAway, message.CloseStatus);
	}

	[Fact]
	public async Task ReceiveMessageAsync_CloseFrameWithStatusAndReason_ReturnsCloseMessageWithReason()
	{
		// Arrange - Close frame with status 1000 and reason "Goodbye"
		byte[] reason = Encoding.UTF8.GetBytes("Goodbye");
		byte[] closeFrame = new byte[2 + 2 + reason.Length];
		closeFrame[0] = 0x88; // FIN=1, Close
		closeFrame[1] = (byte)(2 + reason.Length);
		closeFrame[2] = 0x03; // Status high byte
		closeFrame[3] = 0xE8; // Status low byte (1000)
		Array.Copy(reason, 0, closeFrame, 4, reason.Length);

		_stream.Write(closeFrame, 0, closeFrame.Length);
		_stream.Position = 0;

		// Act
		var message = await _connection.ReceiveMessageAsync(CancellationToken.None);

		// Assert
		Assert.Equal(WebSocketMessageType.Close, message.Type);
		Assert.Equal(WebSocketCloseStatus.NormalClosure, message.CloseStatus);
		Assert.Equal("Goodbye", message.CloseReason);
	}

	[Fact]
	public async Task ReceiveMessageAsync_CloseFrameEmpty_ReturnsCloseMessageWithNullStatus()
	{
		// Arrange - Close frame with no payload
		byte[] closeFrame = new byte[] { 0x88, 0x00 };
		_stream.Write(closeFrame, 0, closeFrame.Length);
		_stream.Position = 0;

		// Act
		var message = await _connection.ReceiveMessageAsync(CancellationToken.None);

		// Assert
		Assert.Equal(WebSocketMessageType.Close, message.Type);
		Assert.Null(message.CloseStatus);
		Assert.Equal("", message.CloseReason);
	}

	#endregion
}
