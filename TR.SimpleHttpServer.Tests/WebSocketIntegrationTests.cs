using System;
using System.Collections.Specialized;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using TR.SimpleHttpServer.WebSocket;

namespace TR.SimpleHttpServer.Tests;

public class WebSocketIntegrationTests : IDisposable
{
	private readonly HttpServer _server;
	private readonly ushort _port;
	private readonly TaskCompletionSource<string> _messageReceived;
	private readonly TaskCompletionSource<bool> _clientConnected;

	public WebSocketIntegrationTests()
	{
		_messageReceived = new TaskCompletionSource<string>();
		_clientConnected = new TaskCompletionSource<bool>();
		_port = GetAvailablePort();
		_server = new HttpServer(_port, HttpHandler, WebSocketHandlerSelectorAsync);
		_server.Start();
	}

	public void Dispose()
	{
		_server.Dispose();
	}

	private static Task<HttpResponse> HttpHandler(HttpRequest request)
	{
		return Task.FromResult(new HttpResponse("200 OK", "text/plain", new NameValueCollection(), "Hello"));
	}

	private async Task<WebSocketHandler?> WebSocketHandlerSelectorAsync(string path)
	{
		// Only handle /ws path
		if (path == "/ws")
		{
			return WebSocketHandlerAsync;
		}
		return null;
	}

	private async Task WebSocketHandlerAsync(HttpRequest request, WebSocketConnection connection)
	{
		_clientConnected.TrySetResult(true);

		while (connection.IsOpen)
		{
			try
			{
				var message = await connection.ReceiveMessageAsync(CancellationToken.None);

				if (message.Type == WebSocketMessageType.Close)
				{
					await connection.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
					// Small delay to allow client to read the close response
					await Task.Delay(100);
					break;
				}

				if (message.Type == WebSocketMessageType.Text)
				{
					string text = message.GetText();
					_messageReceived.TrySetResult(text);

					// Echo the message back
					await connection.SendTextAsync($"Echo: {text}", CancellationToken.None);
				}
			}
			catch (Exception)
			{
				break;
			}
		}
	}

	[Fact]
	public async Task WebSocketHandshake_ValidUpgradeRequest_Returns101()
	{
		// Arrange
		using TcpClient client = new();
		await client.ConnectAsync("127.0.0.1", _port);
		using NetworkStream stream = client.GetStream();
		stream.ReadTimeout = 5000;
		stream.WriteTimeout = 5000;

		string webSocketKey = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
		string[] requestLines = new[]
		{
			$"GET /ws HTTP/1.1",
			$"Host: 127.0.0.1:{_port}",
			"Upgrade: websocket",
			"Connection: Upgrade",
			$"Sec-WebSocket-Key: {webSocketKey}",
			"Sec-WebSocket-Version: 13",
			"",
		};
		string request = string.Join("\r\n", requestLines);

		// Act
		byte[] requestBytes = Encoding.UTF8.GetBytes(request);
		await stream.WriteAsync(requestBytes, 0, requestBytes.Length);
		await stream.FlushAsync();

		// Read response
		byte[] buffer = new byte[1024];
		int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
		string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

		// Assert
		Assert.Contains("HTTP/1.1 101 Switching Protocols", response);
		Assert.Contains("Upgrade: websocket", response);
		Assert.Contains("Connection: Upgrade", response);
		Assert.Contains("Sec-WebSocket-Accept:", response);

		// Verify the accept key is correct
		string expectedAcceptKey = WebSocketHandshake.ComputeAcceptKey(webSocketKey);
		Assert.Contains($"Sec-WebSocket-Accept: {expectedAcceptKey}", response);
	}

	[Fact]
	public async Task WebSocket_SendTextMessage_EchoBack()
	{
		// Arrange
		using TcpClient client = new();
		await client.ConnectAsync("127.0.0.1", _port);
		using NetworkStream stream = client.GetStream();
		stream.ReadTimeout = 5000;
		stream.WriteTimeout = 5000;

		// Perform handshake
		await PerformHandshakeAsync(stream);

		// Wait for server to acknowledge connection
		await WaitWithTimeoutAsync(_clientConnected.Task, TimeSpan.FromSeconds(5));

		// Act - Send a text message (masked, as per RFC 6455 client requirement)
		byte[] payload = Encoding.UTF8.GetBytes("Hello Server!");
		byte[] maskingKey = new byte[] { 0x12, 0x34, 0x56, 0x78 };
		byte[] maskedPayload = WebSocketFrame.ApplyMask(payload, maskingKey);

		// Write frame header
		stream.WriteByte(0x81); // FIN=1, Text
		stream.WriteByte((byte)(0x80 | payload.Length)); // MASK=1, Length
		stream.Write(maskingKey, 0, 4);
		stream.Write(maskedPayload, 0, maskedPayload.Length);
		await stream.FlushAsync();

		// Assert - Server received the message
		string receivedMessage = await WaitWithTimeoutAsync(_messageReceived.Task, TimeSpan.FromSeconds(5));
		Assert.Equal("Hello Server!", receivedMessage);

		// Read echo response
		WebSocketFrameReader reader = new(stream);
		WebSocketFrame responseFrame = await reader.ReadFrameAsync(CancellationToken.None);

		Assert.Equal(WebSocketOpcode.Text, responseFrame.Opcode);
		Assert.Equal("Echo: Hello Server!", Encoding.UTF8.GetString(responseFrame.Payload));
	}

	[Fact]
	public async Task WebSocket_SendCloseFrame_ServerCloses()
	{
		// Arrange
		using TcpClient client = new();
		await client.ConnectAsync("127.0.0.1", _port);
		using NetworkStream stream = client.GetStream();
		stream.ReadTimeout = 5000;
		stream.WriteTimeout = 5000;

		// Perform handshake
		await PerformHandshakeAsync(stream);

		// Wait for server to acknowledge connection
		await WaitWithTimeoutAsync(_clientConnected.Task, TimeSpan.FromSeconds(5));

		// Act - Send close frame
		byte[] closePayload = new byte[] { 0x03, 0xE8 }; // Close status 1000
		byte[] maskingKey = new byte[] { 0x12, 0x34, 0x56, 0x78 };
		byte[] maskedPayload = WebSocketFrame.ApplyMask(closePayload, maskingKey);

		stream.WriteByte(0x88); // FIN=1, Close
		stream.WriteByte((byte)(0x80 | closePayload.Length)); // MASK=1, Length
		stream.Write(maskingKey, 0, 4);
		stream.Write(maskedPayload, 0, maskedPayload.Length);
		await stream.FlushAsync();

		// Assert - Server responds with close frame
		WebSocketFrameReader reader = new(stream);
		WebSocketFrame responseFrame = await reader.ReadFrameAsync(CancellationToken.None);

		Assert.Equal(WebSocketOpcode.Close, responseFrame.Opcode);
	}

	[Fact]
	public async Task WebSocket_SendPing_ReceivePong()
	{
		// This test verifies that the server responds to ping with pong

		// Arrange
		var pongReceived = new TaskCompletionSource<bool>();
		ushort port = GetAvailablePort();

		using HttpServer server = new HttpServer(port, HttpHandler, async (path) =>
		{
			if (path == "/ws")
			{
				return async (req, conn) =>
				{
					while (conn.IsOpen)
					{
						try
						{
							var msg = await conn.ReceiveMessageAsync(CancellationToken.None);
							if (msg.Type == WebSocketMessageType.Close)
								break;
							// The pong is automatically sent by ReceiveMessageAsync
						}
						catch
						{
							break;
						}
					}
				};
			}
			return null;
		});
		server.Start();

		try
		{
			using TcpClient client = new();
			await client.ConnectAsync("127.0.0.1", port);
			using NetworkStream stream = client.GetStream();
			stream.ReadTimeout = 5000;
			stream.WriteTimeout = 5000;

			// Perform handshake
			await PerformHandshakeAsync(stream);

			// Small delay to allow server handler to start
			await Task.Delay(100);

			// Act - Send ping frame
			byte[] pingPayload = Encoding.UTF8.GetBytes("ping");
			byte[] maskingKey = new byte[] { 0x12, 0x34, 0x56, 0x78 };
			byte[] maskedPayload = WebSocketFrame.ApplyMask(pingPayload, maskingKey);

			stream.WriteByte(0x89); // FIN=1, Ping
			stream.WriteByte((byte)(0x80 | pingPayload.Length)); // MASK=1, Length
			stream.Write(maskingKey, 0, 4);
			stream.Write(maskedPayload, 0, maskedPayload.Length);
			await stream.FlushAsync();

			// Assert - Receive pong response
			WebSocketFrameReader reader = new(stream);
			WebSocketFrame responseFrame = await reader.ReadFrameAsync(CancellationToken.None);

			Assert.Equal(WebSocketOpcode.Pong, responseFrame.Opcode);
			Assert.Equal(pingPayload, responseFrame.Payload);
		}
		finally
		{
			server.Stop();
		}
	}

	[Fact]
	public async Task WebSocket_RegularHttpRequest_StillWorks()
	{
		// Arrange
		using TcpClient client = new();
		await client.ConnectAsync("127.0.0.1", _port);
		using NetworkStream stream = client.GetStream();
		stream.ReadTimeout = 5000;
		stream.WriteTimeout = 5000;

		string[] requestLines = new[]
		{
			$"GET /hello HTTP/1.1",
			$"Host: 127.0.0.1:{_port}",
			"",
		};
		string request = string.Join("\r\n", requestLines);

		// Act
		byte[] requestBytes = Encoding.UTF8.GetBytes(request);
		await stream.WriteAsync(requestBytes, 0, requestBytes.Length);
		await stream.FlushAsync();

		// Read response
		byte[] buffer = new byte[1024];
		int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
		string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

		// Assert
		Assert.Contains("HTTP/1.0 200 OK", response);
		Assert.Contains("Hello", response);
	}

	private async Task PerformHandshakeAsync(NetworkStream stream)
	{
		string webSocketKey = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
		string[] requestLines = new[]
		{
			$"GET /ws HTTP/1.1",
			$"Host: 127.0.0.1:{_port}",
			"Upgrade: websocket",
			"Connection: Upgrade",
			$"Sec-WebSocket-Key: {webSocketKey}",
			"Sec-WebSocket-Version: 13",
			"",
		};
		string request = string.Join("\r\n", requestLines);

		byte[] requestBytes = Encoding.UTF8.GetBytes(request);
		await stream.WriteAsync(requestBytes, 0, requestBytes.Length);
		await stream.FlushAsync();

		// Read the HTTP response until we get the double CRLF
		StringBuilder responseBuilder = new();
		byte[] tempBuffer = new byte[1];
		while (true)
		{
			int read = await stream.ReadAsync(tempBuffer, 0, 1);
			if (read == 0)
				break;
			responseBuilder.Append((char)tempBuffer[0]);
			string currentResponse = responseBuilder.ToString();
			if (currentResponse.EndsWith("\r\n\r\n"))
				break;
		}

		string response = responseBuilder.ToString();
		if (!response.Contains("101"))
		{
			throw new InvalidOperationException($"Handshake failed: {response}");
		}
	}

	private static async Task<T> WaitWithTimeoutAsync<T>(Task<T> task, TimeSpan timeout)
	{
		using var cts = new CancellationTokenSource();
		var delayTask = Task.Delay(timeout, cts.Token);

		Task completedTask = await Task.WhenAny(task, delayTask);
		if (completedTask == delayTask)
		{
			throw new TimeoutException();
		}

		cts.Cancel(); // Cancel the delay task
		return await task;
	}

	private static ushort GetAvailablePort()
	{
		using TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
		listener.Start();
		int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return (ushort)port;
	}

	#region Error Case Integration Tests

	[Fact]
	public async Task WebSocketHandshake_InvalidUpgradeHeader_Returns200AsNormalHttp()
	{
		// Arrange - Request with wrong Upgrade header
		using TcpClient client = new();
		await client.ConnectAsync("127.0.0.1", _port);
		using NetworkStream stream = client.GetStream();
		stream.ReadTimeout = 5000;
		stream.WriteTimeout = 5000;

		string[] requestLines = new[]
		{
			$"GET /ws HTTP/1.1",
			$"Host: 127.0.0.1:{_port}",
			"Upgrade: h2c",
			"Connection: Upgrade",
			"Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==",
			"Sec-WebSocket-Version: 13",
			"",
		};
		string request = string.Join("\r\n", requestLines);

		// Act
		byte[] requestBytes = Encoding.UTF8.GetBytes(request);
		await stream.WriteAsync(requestBytes, 0, requestBytes.Length);
		await stream.FlushAsync();

		byte[] buffer = new byte[1024];
		int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
		string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

		// Assert - Should be treated as normal HTTP request, not WebSocket
		Assert.Contains("HTTP/1.0 200", response);
	}

	[Fact]
	public async Task WebSocketHandshake_MissingSecWebSocketKey_Returns200AsNormalHttp()
	{
		// Arrange - Request without Sec-WebSocket-Key
		using TcpClient client = new();
		await client.ConnectAsync("127.0.0.1", _port);
		using NetworkStream stream = client.GetStream();
		stream.ReadTimeout = 5000;
		stream.WriteTimeout = 5000;

		string[] requestLines = new[]
		{
			$"GET /ws HTTP/1.1",
			$"Host: 127.0.0.1:{_port}",
			"Upgrade: websocket",
			"Connection: Upgrade",
			"Sec-WebSocket-Version: 13",
			"",
		};
		string request = string.Join("\r\n", requestLines);

		// Act
		byte[] requestBytes = Encoding.UTF8.GetBytes(request);
		await stream.WriteAsync(requestBytes, 0, requestBytes.Length);
		await stream.FlushAsync();

		byte[] buffer = new byte[1024];
		int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
		string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

		// Assert
		Assert.Contains("HTTP/1.0 200", response);
	}

	[Fact]
	public async Task WebSocketHandshake_WrongVersion_Returns200AsNormalHttp()
	{
		// Arrange - Request with wrong WebSocket version
		using TcpClient client = new();
		await client.ConnectAsync("127.0.0.1", _port);
		using NetworkStream stream = client.GetStream();
		stream.ReadTimeout = 5000;
		stream.WriteTimeout = 5000;

		string[] requestLines = new[]
		{
			$"GET /ws HTTP/1.1",
			$"Host: 127.0.0.1:{_port}",
			"Upgrade: websocket",
			"Connection: Upgrade",
			"Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==",
			"Sec-WebSocket-Version: 8",
			"",
		};
		string request = string.Join("\r\n", requestLines);

		// Act
		byte[] requestBytes = Encoding.UTF8.GetBytes(request);
		await stream.WriteAsync(requestBytes, 0, requestBytes.Length);
		await stream.FlushAsync();

		byte[] buffer = new byte[1024];
		int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
		string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

		// Assert
		Assert.Contains("HTTP/1.0 200", response);
	}

	[Fact]
	public async Task WebSocketHandshake_PostMethod_Returns200AsNormalHttp()
	{
		// Arrange - POST request (WebSocket requires GET)
		using TcpClient client = new();
		await client.ConnectAsync("127.0.0.1", _port);
		using NetworkStream stream = client.GetStream();
		stream.ReadTimeout = 5000;
		stream.WriteTimeout = 5000;

		string[] requestLines = new[]
		{
			"POST /ws HTTP/1.1",
			$"Host: 127.0.0.1:{_port}",
			"Upgrade: websocket",
			"Connection: Upgrade",
			"Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==",
			"Sec-WebSocket-Version: 13",
			"",
		};
		string request = string.Join("\r\n", requestLines);   // Act
		byte[] requestBytes = Encoding.UTF8.GetBytes(request);
		await stream.WriteAsync(requestBytes, 0, requestBytes.Length);
		await stream.FlushAsync();

		byte[] buffer = new byte[1024];
		int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
		string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

		// Assert
		Assert.Contains("HTTP/1.0 200", response);
	}

	[Fact]
	public async Task WebSocket_NonWebSocketPath_ReturnsFallbackResponse()
	{
		// Arrange - Request to path without WebSocket handler
		using TcpClient client = new();
		await client.ConnectAsync("127.0.0.1", _port);
		using NetworkStream stream = client.GetStream();
		stream.ReadTimeout = 5000;
		stream.WriteTimeout = 5000;

		string[] requestLines = new[]
		{
			$"GET /not-ws HTTP/1.1",
			$"Host: 127.0.0.1:{_port}",
			"Upgrade: websocket",
			"Connection: Upgrade",
			"Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==",
			"Sec-WebSocket-Version: 13",
			"",
		};
		string request = string.Join("\r\n", requestLines);

		// Act
		byte[] requestBytes = Encoding.UTF8.GetBytes(request);
		await stream.WriteAsync(requestBytes, 0, requestBytes.Length);
		await stream.FlushAsync();

		byte[] buffer = new byte[1024];
		int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
		string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

		// Assert - Since /not-ws has no handler, returns 404 instead
		Assert.True(response.Contains("HTTP/1.0 404") || response.Contains("HTTP/1.0 200"));
	}

	[Fact]
	public async Task WebSocket_ClientDisconnectsUnexpectedly_ServerHandlesGracefully()
	{
		// Arrange
		ushort port = GetAvailablePort();
		var handlerCompleted = new TaskCompletionSource<bool>();
		Exception? handlerException = null;

		using HttpServer server = new HttpServer(port, HttpHandler, async (path) =>
		{
			if (path == "/ws")
			{
				return async (req, conn) =>
				{
					try
					{
						// Try to receive message - should fail when client disconnects
						await conn.ReceiveMessageAsync(CancellationToken.None);
					}
					catch (Exception ex)
					{
						handlerException = ex;
					}
					finally
					{
						handlerCompleted.TrySetResult(true);
					}
				};
			}
			return null;
		});
		server.Start();

		try
		{
			// Connect and complete handshake
			using TcpClient client = new();
			await client.ConnectAsync("127.0.0.1", port);
			using NetworkStream stream = client.GetStream();
			stream.ReadTimeout = 5000;
			stream.WriteTimeout = 5000;

			await PerformHandshakeAsync(stream, port);

			// Small delay to let server handler start
			await Task.Delay(100);

			// Abruptly close connection without sending close frame
			client.Close();

			// Wait for handler to complete
			await WaitWithTimeoutAsync(handlerCompleted.Task, TimeSpan.FromSeconds(5));

			// Assert - Handler should have caught an exception
			Assert.NotNull(handlerException);
		}
		finally
		{
			server.Stop();
		}
	}

	[Fact]
	public async Task WebSocket_SendLargeMessage_TransmitsCorrectly()
	{
		// Arrange
		ushort port = GetAvailablePort();
		var messageReceived = new TaskCompletionSource<string>();
		string largeMessage = new string('X', 10000);

		using HttpServer server = new HttpServer(port, HttpHandler, async (path) =>
		{
			if (path == "/ws")
			{
				return async (req, conn) =>
				{
					try
					{
						var msg = await conn.ReceiveMessageAsync(CancellationToken.None);
						if (msg.Type == WebSocketMessageType.Text)
						{
							messageReceived.TrySetResult(msg.GetText());
							await conn.SendTextAsync("OK", CancellationToken.None);
						}
					}
					catch { }
				};
			}
			return null;
		});
		server.Start();

		try
		{
			using TcpClient client = new();
			await client.ConnectAsync("127.0.0.1", port);
			using NetworkStream stream = client.GetStream();
			stream.ReadTimeout = 10000;
			stream.WriteTimeout = 10000;

			await PerformHandshakeAsync(stream, port);
			await Task.Delay(100);

			// Send large message (requires 16-bit length encoding)
			byte[] payload = Encoding.UTF8.GetBytes(largeMessage);
			byte[] maskingKey = new byte[] { 0x12, 0x34, 0x56, 0x78 };
			byte[] maskedPayload = WebSocketFrame.ApplyMask(payload, maskingKey);

			stream.WriteByte(0x81); // FIN=1, Text
			stream.WriteByte(0xFE); // MASK=1, Extended length (126)
			stream.WriteByte((byte)(payload.Length >> 8)); // Length high byte
			stream.WriteByte((byte)(payload.Length & 0xFF)); // Length low byte
			stream.Write(maskingKey, 0, 4);
			stream.Write(maskedPayload, 0, maskedPayload.Length);
			await stream.FlushAsync();

			// Wait for server to receive and process
			string receivedMessage = await WaitWithTimeoutAsync(messageReceived.Task, TimeSpan.FromSeconds(10));

			// Assert
			Assert.Equal(largeMessage, receivedMessage);
		}
		finally
		{
			server.Stop();
		}
	}

	[Fact]
	public async Task WebSocket_SendBinaryMessage_TransmitsCorrectly()
	{
		// Arrange
		ushort port = GetAvailablePort();
		var messageReceived = new TaskCompletionSource<byte[]>();

		using HttpServer server = new HttpServer(port, HttpHandler, async (path) =>
		{
			if (path == "/ws")
			{
				return async (req, conn) =>
				{
					try
					{
						var msg = await conn.ReceiveMessageAsync(CancellationToken.None);
						if (msg.Type == WebSocketMessageType.Binary)
						{
							messageReceived.TrySetResult(msg.Data);
							await conn.SendBinaryAsync(msg.Data, CancellationToken.None);
						}
					}
					catch { }
				};
			}
			return null;
		});
		server.Start();

		try
		{
			using TcpClient client = new();
			await client.ConnectAsync("127.0.0.1", port);
			using NetworkStream stream = client.GetStream();
			stream.ReadTimeout = 5000;
			stream.WriteTimeout = 5000;

			await PerformHandshakeAsync(stream, port);
			await Task.Delay(100);

			// Send binary message
			byte[] payload = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0xFD };
			byte[] maskingKey = new byte[] { 0x12, 0x34, 0x56, 0x78 };
			byte[] maskedPayload = WebSocketFrame.ApplyMask(payload, maskingKey);

			stream.WriteByte(0x82); // FIN=1, Binary
			stream.WriteByte((byte)(0x80 | payload.Length)); // MASK=1, Length
			stream.Write(maskingKey, 0, 4);
			stream.Write(maskedPayload, 0, maskedPayload.Length);
			await stream.FlushAsync();

			// Wait for server to receive
			byte[] receivedData = await WaitWithTimeoutAsync(messageReceived.Task, TimeSpan.FromSeconds(5));

			// Assert
			Assert.Equal(payload, receivedData);
		}
		finally
		{
			server.Stop();
		}
	}

	[Fact]
	public async Task WebSocket_UnmaskedClientFrame_ServerReceivesMessage()
	{
		// Note: RFC 6455 says client MUST mask frames, but some implementations may be lenient
		// This test documents actual behavior

		// Arrange
		ushort port = GetAvailablePort();
		var messageReceived = new TaskCompletionSource<string>();

		using HttpServer server = new HttpServer(port, HttpHandler, async (path) =>
		{
			if (path == "/ws")
			{
				return async (req, conn) =>
				{
					try
					{
						var msg = await conn.ReceiveMessageAsync(CancellationToken.None);
						if (msg.Type == WebSocketMessageType.Text)
						{
							messageReceived.TrySetResult(msg.GetText());
						}
					}
					catch { }
				};
			}
			return null;
		});
		server.Start();

		try
		{
			using TcpClient client = new();
			await client.ConnectAsync("127.0.0.1", port);
			using NetworkStream stream = client.GetStream();
			stream.ReadTimeout = 5000;
			stream.WriteTimeout = 5000;

			await PerformHandshakeAsync(stream, port);
			await Task.Delay(100);

			// Send unmasked frame (technically violates RFC 6455 for client)
			byte[] payload = Encoding.UTF8.GetBytes("Test");
			stream.WriteByte(0x81); // FIN=1, Text
			stream.WriteByte((byte)payload.Length); // MASK=0, Length
			stream.Write(payload, 0, payload.Length);
			await stream.FlushAsync();

			// Server should still receive the message
			string receivedMessage = await WaitWithTimeoutAsync(messageReceived.Task, TimeSpan.FromSeconds(5));
			Assert.Equal("Test", receivedMessage);
		}
		finally
		{
			server.Stop();
		}
	}

	private async Task PerformHandshakeAsync(NetworkStream stream, ushort port)
	{
		string webSocketKey = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
		string[] requestLines = new[]
		{
			$"GET /ws HTTP/1.1",
			$"Host: 127.0.0.1:{port}",
			"Upgrade: websocket",
			"Connection: Upgrade",
			$"Sec-WebSocket-Key: {webSocketKey}",
			"Sec-WebSocket-Version: 13",
			"",
		};
		string request = string.Join("\r\n", requestLines);

		byte[] requestBytes = Encoding.UTF8.GetBytes(request);
		await stream.WriteAsync(requestBytes, 0, requestBytes.Length);
		await stream.FlushAsync();

		StringBuilder responseBuilder = new();
		byte[] tempBuffer = new byte[1];
		while (true)
		{
			int read = await stream.ReadAsync(tempBuffer, 0, 1);
			if (read == 0)
				break;
			responseBuilder.Append((char)tempBuffer[0]);
			string currentResponse = responseBuilder.ToString();
			if (currentResponse.EndsWith("\r\n\r\n"))
				break;
		}

		string response = responseBuilder.ToString();
		if (!response.Contains("101"))
		{
			throw new InvalidOperationException($"Handshake failed: {response}");
		}
	}

	#endregion
}
