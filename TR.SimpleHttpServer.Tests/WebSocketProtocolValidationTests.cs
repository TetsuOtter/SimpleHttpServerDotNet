using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using TR.SimpleHttpServer.WebSocket;

namespace TR.SimpleHttpServer.Tests;

/// <summary>
/// Tests covering RFC 6455 protocol validation in WebSocketConnection.
/// </summary>
public class WebSocketProtocolValidationTests
{
	private static WebSocketConnection NewConnection(MemoryStream stream, WebSocketOptions? options = null)
	{
		options ??= new WebSocketOptions { RequireMasking = false };
		return new WebSocketConnection(stream, options);
	}

	private static byte[] ReadCloseFrameFromTail(MemoryStream stream, long readBeforePosition)
	{
		// Returns the close frame the server appended after the parsed input.
		stream.Position = readBeforePosition;
		using var ms = new MemoryStream();
		stream.CopyTo(ms);
		return ms.ToArray();
	}

	private static (WebSocketCloseStatus status, string reason) ParseCloseFrame(byte[] frameBytes)
	{
		Assert.True(frameBytes.Length >= 2, "Close frame missing");
		Assert.Equal(0x88, frameBytes[0]);
		int payloadLen = frameBytes[1] & 0x7F;
		Assert.True(frameBytes.Length >= 2 + payloadLen);
		ushort code = (ushort)((frameBytes[2] << 8) | frameBytes[3]);
		string reason = payloadLen > 2 ? Encoding.UTF8.GetString(frameBytes, 4, payloadLen - 2) : "";
		return ((WebSocketCloseStatus)code, reason);
	}

	[Fact]
	public async Task ReceiveMessageAsync_Rsv1Set_ClosesWithProtocolError()
	{
		// FIN=1, RSV1=1, Text, len=0
		byte[] frame = [0xC1, 0x00];
		using var stream = new MemoryStream();
		stream.Write(frame, 0, frame.Length);
		stream.Position = 0;
		long inputEnd = frame.Length;

		using var conn = NewConnection(stream);
		var ex = await Assert.ThrowsAsync<WebSocketProtocolException>(() => conn.ReceiveMessageAsync(CancellationToken.None));
		Assert.Equal(WebSocketCloseStatus.ProtocolError, ex.CloseStatus);

		var (code, _) = ParseCloseFrame(ReadCloseFrameFromTail(stream, inputEnd));
		Assert.Equal(WebSocketCloseStatus.ProtocolError, code);
	}

	[Theory]
	[InlineData(0xA0)] // RSV2
	[InlineData(0x90)] // RSV3
	[InlineData(0x70)] // RSV1+2+3
	public async Task ReceiveMessageAsync_AnyRsvSet_ClosesWithProtocolError(int firstByte)
	{
		byte[] frame = [(byte)(0x80 | firstByte | 0x01), 0x00]; // FIN + RSV + Text
		using var stream = new MemoryStream();
		stream.Write(frame, 0, frame.Length);
		stream.Position = 0;

		using var conn = NewConnection(stream);
		var ex = await Assert.ThrowsAsync<WebSocketProtocolException>(() => conn.ReceiveMessageAsync(CancellationToken.None));
		Assert.Equal(WebSocketCloseStatus.ProtocolError, ex.CloseStatus);
	}

	[Theory]
	[InlineData(0x83)] // 0x3 reserved data
	[InlineData(0x84)]
	[InlineData(0x87)]
	[InlineData(0x8B)] // 0xB reserved control
	[InlineData(0x8F)]
	public async Task ReceiveMessageAsync_ReservedOpcode_ClosesWithProtocolError(int firstByte)
	{
		byte[] frame = [(byte)firstByte, 0x00];
		using var stream = new MemoryStream();
		stream.Write(frame, 0, frame.Length);
		stream.Position = 0;

		using var conn = NewConnection(stream);
		var ex = await Assert.ThrowsAsync<WebSocketProtocolException>(() => conn.ReceiveMessageAsync(CancellationToken.None));
		Assert.Equal(WebSocketCloseStatus.ProtocolError, ex.CloseStatus);
	}

	[Fact]
	public async Task ReceiveMessageAsync_FragmentedControlFrame_ClosesWithProtocolError()
	{
		// FIN=0, Ping (control frames must not be fragmented)
		byte[] frame = [0x09, 0x00];
		using var stream = new MemoryStream();
		stream.Write(frame, 0, frame.Length);
		stream.Position = 0;

		using var conn = NewConnection(stream);
		var ex = await Assert.ThrowsAsync<WebSocketProtocolException>(() => conn.ReceiveMessageAsync(CancellationToken.None));
		Assert.Equal(WebSocketCloseStatus.ProtocolError, ex.CloseStatus);
	}

	[Fact]
	public async Task ReceiveMessageAsync_OversizedControlFrame_ClosesWithProtocolError()
	{
		// FIN=1, Ping, length=126 (extended length used) → control payload > 125 is invalid
		byte[] payload = new byte[126];
		using var stream = new MemoryStream();
		stream.WriteByte(0x89); // FIN=1, Ping
		stream.WriteByte(126);  // 16-bit extended length
		stream.WriteByte(0x00);
		stream.WriteByte(126);
		stream.Write(payload, 0, payload.Length);
		stream.Position = 0;

		using var conn = NewConnection(stream);
		var ex = await Assert.ThrowsAsync<WebSocketProtocolException>(() => conn.ReceiveMessageAsync(CancellationToken.None));
		Assert.Equal(WebSocketCloseStatus.ProtocolError, ex.CloseStatus);
	}

	[Fact]
	public async Task ReceiveMessageAsync_DataFrameMidFragment_ClosesWithProtocolError()
	{
		using var stream = new MemoryStream();
		// FIN=0, Text "ab"
		stream.Write([0x01, 0x02, 0x61, 0x62], 0, 4);
		// FIN=1, Text "cd" - illegal: must be Continuation
		stream.Write([0x81, 0x02, 0x63, 0x64], 0, 4);
		stream.Position = 0;

		using var conn = NewConnection(stream);
		var ex = await Assert.ThrowsAsync<WebSocketProtocolException>(() => conn.ReceiveMessageAsync(CancellationToken.None));
		Assert.Equal(WebSocketCloseStatus.ProtocolError, ex.CloseStatus);
	}

	[Fact]
	public async Task ReceiveMessageAsync_UnmaskedFrame_WhenMaskingRequired_ClosesWithProtocolError()
	{
		// FIN=1, Text "x" unmasked
		byte[] frame = [0x81, 0x01, 0x78];
		using var stream = new MemoryStream();
		stream.Write(frame, 0, frame.Length);
		stream.Position = 0;

		// Default options: RequireMasking = true
		using var conn = new WebSocketConnection(stream, new WebSocketOptions());
		var ex = await Assert.ThrowsAsync<WebSocketProtocolException>(() => conn.ReceiveMessageAsync(CancellationToken.None));
		Assert.Equal(WebSocketCloseStatus.ProtocolError, ex.CloseStatus);
	}

	[Fact]
	public async Task ReceiveMessageAsync_InvalidUtf8InTextFrame_ClosesWithInvalidPayloadData()
	{
		// FIN=1, Text, payload = 0xFF (invalid UTF-8 start byte)
		byte[] frame = [0x81, 0x01, 0xFF];
		using var stream = new MemoryStream();
		stream.Write(frame, 0, frame.Length);
		stream.Position = 0;

		using var conn = NewConnection(stream);
		var ex = await Assert.ThrowsAsync<WebSocketProtocolException>(() => conn.ReceiveMessageAsync(CancellationToken.None));
		Assert.Equal(WebSocketCloseStatus.InvalidPayloadData, ex.CloseStatus);
	}

	[Fact]
	public async Task ReceiveMessageAsync_ClosePayloadLength1_ClosesWithProtocolError()
	{
		// FIN=1, Close, length=1
		byte[] frame = [0x88, 0x01, 0x00];
		using var stream = new MemoryStream();
		stream.Write(frame, 0, frame.Length);
		stream.Position = 0;

		using var conn = NewConnection(stream);
		var ex = await Assert.ThrowsAsync<WebSocketProtocolException>(() => conn.ReceiveMessageAsync(CancellationToken.None));
		Assert.Equal(WebSocketCloseStatus.ProtocolError, ex.CloseStatus);
	}

	[Theory]
	[InlineData(999)]
	[InlineData(1004)]
	[InlineData(1005)]
	[InlineData(1006)]
	[InlineData(1015)]
	[InlineData(2000)]
	[InlineData(5000)]
	public async Task ReceiveMessageAsync_InvalidCloseCode_ClosesWithProtocolError(int code)
	{
		using var stream = new MemoryStream();
		stream.WriteByte(0x88); // Close
		stream.WriteByte(0x02); // length=2
		stream.WriteByte((byte)((code >> 8) & 0xFF));
		stream.WriteByte((byte)(code & 0xFF));
		stream.Position = 0;

		using var conn = NewConnection(stream);
		var ex = await Assert.ThrowsAsync<WebSocketProtocolException>(() => conn.ReceiveMessageAsync(CancellationToken.None));
		Assert.Equal(WebSocketCloseStatus.ProtocolError, ex.CloseStatus);
	}

	[Theory]
	[InlineData(1000)]
	[InlineData(1011)]
	[InlineData(1012)]
	[InlineData(3000)]
	[InlineData(4999)]
	public async Task ReceiveMessageAsync_ValidCloseCode_ReturnsCloseMessage(int code)
	{
		using var stream = new MemoryStream();
		stream.WriteByte(0x88);
		stream.WriteByte(0x02);
		stream.WriteByte((byte)((code >> 8) & 0xFF));
		stream.WriteByte((byte)(code & 0xFF));
		stream.Position = 0;

		using var conn = NewConnection(stream);
		var message = await conn.ReceiveMessageAsync(CancellationToken.None);
		Assert.Equal(WebSocketMessageType.Close, message.Type);
		Assert.Equal((WebSocketCloseStatus)code, message.CloseStatus);
	}

	[Fact]
	public async Task ReceiveMessageAsync_InvalidUtf8InCloseReason_ClosesWithInvalidPayloadData()
	{
		using var stream = new MemoryStream();
		stream.WriteByte(0x88);
		stream.WriteByte(0x03);    // length=3 (status + 1-byte invalid utf8)
		stream.WriteByte(0x03);    // 1000 high
		stream.WriteByte(0xE8);    // 1000 low
		stream.WriteByte(0xFF);    // invalid UTF-8
		stream.Position = 0;

		using var conn = NewConnection(stream);
		var ex = await Assert.ThrowsAsync<WebSocketProtocolException>(() => conn.ReceiveMessageAsync(CancellationToken.None));
		Assert.Equal(WebSocketCloseStatus.InvalidPayloadData, ex.CloseStatus);
	}

	[Fact]
	public async Task ReceiveMessageAsync_FrameExceedsMaxFrameSize_ClosesWithMessageTooBig()
	{
		// Configure tiny frame limit and send a frame whose declared length exceeds it.
		var options = new WebSocketOptions { RequireMasking = false, MaxFrameSize = 100 };

		using var stream = new MemoryStream();
		stream.WriteByte(0x82);    // FIN=1, Binary
		stream.WriteByte(126);     // 16-bit extended length
		stream.WriteByte(0x01);    // length high (256 + ...)
		stream.WriteByte(0xF4);    // length low → 500
		stream.Position = 0;

		using var conn = new WebSocketConnection(stream, options);
		var ex = await Assert.ThrowsAsync<WebSocketProtocolException>(() => conn.ReceiveMessageAsync(CancellationToken.None));
		Assert.Equal(WebSocketCloseStatus.MessageTooBig, ex.CloseStatus);
	}

	[Fact]
	public async Task ReceiveMessageAsync_AccumulatedMessageExceedsMaxMessageSize_ClosesWithMessageTooBig()
	{
		// Two fragments individually under the per-frame limit but combined exceed MaxMessageSize.
		var options = new WebSocketOptions { RequireMasking = false, MaxFrameSize = 100, MaxMessageSize = 10 };

		using var stream = new MemoryStream();
		// FIN=0, Binary, len=8
		stream.WriteByte(0x02);
		stream.WriteByte(0x08);
		stream.Write(new byte[8], 0, 8);
		// FIN=1, Continuation, len=8 → total 16 > 10
		stream.WriteByte(0x80);
		stream.WriteByte(0x08);
		stream.Write(new byte[8], 0, 8);
		stream.Position = 0;

		using var conn = new WebSocketConnection(stream, options);
		var ex = await Assert.ThrowsAsync<WebSocketProtocolException>(() => conn.ReceiveMessageAsync(CancellationToken.None));
		Assert.Equal(WebSocketCloseStatus.MessageTooBig, ex.CloseStatus);
	}

	[Fact]
	public async Task ReceiveMessageAsync_AutoRespondToClose_EchoesCloseFrame()
	{
		using var stream = new MemoryStream();
		stream.Write([0x88, 0x02, 0x03, 0xE8], 0, 4); // Close 1000
		stream.Position = 0;
		long inputEnd = 4;

		using var conn = NewConnection(stream);
		var msg = await conn.ReceiveMessageAsync(CancellationToken.None);
		Assert.Equal(WebSocketMessageType.Close, msg.Type);

		var (status, _) = ParseCloseFrame(ReadCloseFrameFromTail(stream, inputEnd));
		Assert.Equal(WebSocketCloseStatus.NormalClosure, status);
	}

	[Fact]
	public async Task ReceiveMessageAsync_AutoRespondDisabled_DoesNotSendCloseAutomatically()
	{
		var options = new WebSocketOptions { RequireMasking = false, AutoRespondToClose = false };

		using var stream = new MemoryStream();
		stream.Write([0x88, 0x02, 0x03, 0xE8], 0, 4);
		stream.Position = 0;
		long inputEnd = 4;

		using var conn = new WebSocketConnection(stream, options);
		var msg = await conn.ReceiveMessageAsync(CancellationToken.None);
		Assert.Equal(WebSocketMessageType.Close, msg.Type);

		// Stream should have nothing past the input - nothing written.
		stream.Position = inputEnd;
		Assert.Equal(-1, stream.ReadByte());
	}

	[Fact]
	public async Task SendPingAsync_PayloadOver125_ThrowsArgumentException()
	{
		using var stream = new MemoryStream();
		using var conn = NewConnection(stream);
		await Assert.ThrowsAsync<ArgumentException>(() => conn.SendPingAsync(new byte[126], CancellationToken.None));
	}

	[Fact]
	public async Task ReceiveMessageAsync_PingFrame_AwaitedPongIsObservable()
	{
		// Verifies the pong is written before ReceiveMessageAsync continues to read the next frame.
		byte[] pingPayload = [0x01, 0x02, 0x03];
		using var stream = new MemoryStream();
		stream.Write([0x89, (byte)pingPayload.Length, .. pingPayload], 0, 2 + pingPayload.Length);
		stream.Position = 0;
		long inputEnd = 2 + pingPayload.Length;

		using var conn = NewConnection(stream);
		try { await conn.ReceiveMessageAsync(CancellationToken.None); }
		catch (EndOfStreamException) { /* expected - no follow-up frame */ }

		// Pong was written at the tail.
		stream.Position = inputEnd;
		using var tail = new MemoryStream();
		stream.CopyTo(tail);
		byte[] pong = tail.ToArray();
		Assert.True(pong.Length >= 2 + pingPayload.Length);
		Assert.Equal(0x8A, pong[0]); // FIN=1, Pong
		Assert.Equal(pingPayload.Length, pong[1]);
	}
}
