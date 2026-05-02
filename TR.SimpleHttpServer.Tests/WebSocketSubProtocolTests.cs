using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using TR.SimpleHttpServer.WebSocket;

namespace TR.SimpleHttpServer.Tests;

public class WebSocketSubProtocolTests
{
	[Fact]
	public void SelectSubProtocol_ClientPreferenceWinsOverServerOrder()
	{
		// Client requests b, a → server supports a, b → client preference wins → 'b'
		NameValueCollection headers = new()
		{
			{ "Sec-WebSocket-Protocol", "b, a" }
		};
		HttpRequest req = new("GET", "/ws", headers, new NameValueCollection(), Array.Empty<byte>());

		string? selected = WebSocketHandshake.SelectSubProtocol(req, new[] { "a", "b" });

		Assert.Equal("b", selected);
	}

	[Fact]
	public void SelectSubProtocol_NoOverlap_ReturnsNull()
	{
		NameValueCollection headers = new()
		{
			{ "Sec-WebSocket-Protocol", "x, y" }
		};
		HttpRequest req = new("GET", "/ws", headers, new NameValueCollection(), Array.Empty<byte>());

		Assert.Null(WebSocketHandshake.SelectSubProtocol(req, new[] { "a", "b" }));
	}

	[Fact]
	public void SelectSubProtocol_NoClientHeader_ReturnsNull()
	{
		HttpRequest req = new("GET", "/ws", new NameValueCollection(), new NameValueCollection(), Array.Empty<byte>());

		Assert.Null(WebSocketHandshake.SelectSubProtocol(req, new[] { "a" }));
	}

	[Fact]
	public void SelectSubProtocol_ServerHasNoProtocols_ReturnsNull()
	{
		NameValueCollection headers = new()
		{
			{ "Sec-WebSocket-Protocol", "a" }
		};
		HttpRequest req = new("GET", "/ws", headers, new NameValueCollection(), Array.Empty<byte>());

		Assert.Null(WebSocketHandshake.SelectSubProtocol(req, null));
		Assert.Null(WebSocketHandshake.SelectSubProtocol(req, Array.Empty<string>()));
	}

	[Fact]
	public void GetRequestedSubProtocols_ParsesAndTrims()
	{
		NameValueCollection headers = new()
		{
			{ "Sec-WebSocket-Protocol", "  chat , superchat ,  " }
		};
		HttpRequest req = new("GET", "/ws", headers, new NameValueCollection(), Array.Empty<byte>());

		IReadOnlyList<string> result = WebSocketHandshake.GetRequestedSubProtocols(req);

		Assert.Equal(new[] { "chat", "superchat" }, result);
	}

	[Fact]
	public void CreateUpgradeResponseHeaders_IncludesSelectedSubProtocol()
	{
		NameValueCollection headers = WebSocketHandshake.CreateUpgradeResponseHeaders("dGhlIHNhbXBsZSBub25jZQ==", "chat");
		Assert.Equal("chat", headers["Sec-WebSocket-Protocol"]);
	}

	[Fact]
	public void CreateUpgradeResponseHeaders_NoSubProtocol_OmitsHeader()
	{
		NameValueCollection headers = WebSocketHandshake.CreateUpgradeResponseHeaders("dGhlIHNhbXBsZSBub25jZQ==", null);
		Assert.Null(headers["Sec-WebSocket-Protocol"]);
	}

	[Fact]
	public async Task ServerNegotiatesSubProtocolEndToEnd()
	{
		ushort port = GetAvailablePort();
		var connectedTcs = new TaskCompletionSource<string?>();

		WebSocketOptions options = new() { SupportedSubProtocols = new[] { "chat", "superchat" } };

		using HttpServer server = new(
			System.Net.IPAddress.Loopback,
			port,
			req => Task.FromResult(new HttpResponse("200 OK", "text/plain", new NameValueCollection(), "ok")),
			async path => path == "/ws"
				? (req, conn) => { connectedTcs.TrySetResult(conn.SelectedSubProtocol); return Task.CompletedTask; }
				: null,
			options
		);
		server.Start();

		try
		{
			using TcpClient client = new();
			await client.ConnectAsync("127.0.0.1", port);
			using NetworkStream stream = client.GetStream();
			stream.ReadTimeout = 5000;
			stream.WriteTimeout = 5000;

			string key = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
			string request =
				"GET /ws HTTP/1.1\r\n" +
				$"Host: 127.0.0.1:{port}\r\n" +
				"Upgrade: websocket\r\n" +
				"Connection: Upgrade\r\n" +
				$"Sec-WebSocket-Key: {key}\r\n" +
				"Sec-WebSocket-Version: 13\r\n" +
				"Sec-WebSocket-Protocol: superchat, chat\r\n" +
				"\r\n";

			byte[] reqBytes = Encoding.UTF8.GetBytes(request);
			await stream.WriteAsync(reqBytes, 0, reqBytes.Length);
			await stream.FlushAsync();

			// Read response headers
			StringBuilder sb = new();
			byte[] one = new byte[1];
			while (true)
			{
				int read = await stream.ReadAsync(one, 0, 1);
				if (read == 0) break;
				sb.Append((char)one[0]);
				if (sb.ToString().EndsWith("\r\n\r\n")) break;
			}

			string response = sb.ToString();
			Assert.Contains("HTTP/1.1 101", response);
			Assert.Contains("Sec-WebSocket-Protocol: superchat", response);

			string? observed = await connectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
			Assert.Equal("superchat", observed);
		}
		finally
		{
			server.Stop();
		}
	}

	private static ushort GetAvailablePort()
	{
		using TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
		listener.Start();
		int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return (ushort)port;
	}
}
