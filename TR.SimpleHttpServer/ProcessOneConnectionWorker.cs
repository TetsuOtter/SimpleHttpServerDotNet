using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace TR.SimpleHttpServer;

internal class ProcessOneConnectionWorker
(
	TcpClient client,
	CancellationToken cancellationToken,
	HttpConnectionHandler handler,
	WebSocketRequestHandler? canUpgradeToWebSocket,
	WebSocketHandler? webSocketHandler
) : IDisposable
{
	private readonly TcpClient client = client;
	private readonly NetworkStream stream = client.GetStream();
	private readonly CancellationToken cancellationToken = cancellationToken;
	private readonly HttpConnectionHandler handler = handler;
	private readonly WebSocketRequestHandler? CanUpgradeToWebSocket = canUpgradeToWebSocket;
	private readonly WebSocketHandler? WebSocketHandler = webSocketHandler;
	bool isWebSocket = false;

	public async Task ProcessAsync()
	{
		if (disposedValue)
			throw new ObjectDisposedException(nameof(ProcessOneConnectionWorker));

		if (!client.Connected)
			throw new InvalidOperationException("Client is not connected.");

		cancellationToken.ThrowIfCancellationRequested();

		stream.ReadTimeout = 2000;
		stream.WriteTimeout = 2000;
		MyStreamReader reader = new(stream, cancellationToken, Encoding.UTF8);
		await ProcessAsync(reader);
		if (!isWebSocket)
			await stream.FlushAsync(cancellationToken);
	}

	private async Task ProcessAsync(MyStreamReader reader)
	{
		bool isHeadMethod;
		string method;
		string rawPath;
		string httpVersion;
		byte[] body = [];
		NameValueCollection headers = [];
		string requestLine = await reader.ReadLineAsync(forceRead: true);
		if (requestLine is null)
		{
			await WriteResponseAsync(HttpStatusCode.BadRequest, "text/plain", "Bad Request (empty request)");
			return;
		}

		{
			requestLine = requestLine.Trim();
			int firstSpaceIndex = requestLine.IndexOf(' ');
			if (firstSpaceIndex == -1)
			{
				await WriteResponseAsync(HttpStatusCode.BadRequest, "text/plain", "Bad Request (invalid request line - no space character)");
				return;
			}
			int lastSpaceIndex = requestLine.LastIndexOf(' ');
			if (firstSpaceIndex == lastSpaceIndex)
			{
				await WriteResponseAsync(HttpStatusCode.BadRequest, "text/plain", "Bad Request (invalid request line - only one space character)");
				return;
			}

			method = requestLine.Substring(0, firstSpaceIndex).ToUpper();
			rawPath = requestLine.Substring(firstSpaceIndex + 1, lastSpaceIndex - firstSpaceIndex - 1).Trim();
			httpVersion = requestLine.Substring(lastSpaceIndex + 1);
			isHeadMethod = method == "HEAD";
		}

		while (true)
		{
			string? headerLine = await reader.ReadLineAsync();
			if (headerLine is null)
			{
				await WriteResponseAsync(HttpStatusCode.BadRequest, "text/plain", "Bad Request (empty header)");
				return;
			}

			if (headerLine == "")
				break;

			string[] headerParts = headerLine.Split([':'], 2);
			if (headerParts.Length != 2)
			{
				await WriteResponseAsync(HttpStatusCode.BadRequest, "text/plain", "Bad Request (invalid header)");
				return;
			}

			headers.Add(headerParts[0], headerParts[1].Trim());
		}

		if (headers.GetValues("Content-Length") is string[] contentLengthValues)
		{
			if (contentLengthValues.Length != 1)
			{
				await WriteResponseAsync(HttpStatusCode.BadRequest, "text/plain", "Bad Request (invalid Content-Length header)");
				return;
			}

			if (!long.TryParse(contentLengthValues[0], out long contentLength))
			{
				await WriteResponseAsync(HttpStatusCode.BadRequest, "text/plain", "Bad Request (invalid Content-Length header)");
				return;
			}

			if (0 < contentLength)
			{
				body = await reader.ReadRemainingBytesAsync();
			}
		}

		string path = HttpUtility.UrlDecode(rawPath);
		string decodedPath = path;
		string query = "";
		int questionMarkIndex = path.IndexOf('?');
		if (questionMarkIndex != -1)
		{
			query = path.Substring(questionMarkIndex + 1);
			path = path.Substring(0, questionMarkIndex);
		}

		NameValueCollection queryString = HttpUtility.ParseQueryString(query);
		HttpRequest request = new(httpVersion, method, path, headers, queryString, body);
		try
		{
			HttpResponse? webSocketResponse = await CheckAndProcessWebSocketAsync(request);
			HttpResponse response = webSocketResponse ?? await handler(request);
			await WriteResponseAsync(response, isHead: isHeadMethod);
			isWebSocket = webSocketResponse?.StatusCode == HttpStatusCode.SwitchingProtocols;
			if (!isWebSocket)
				return;
		}
		catch (Exception ex)
		{
			await WriteResponseAsync(HttpStatusCode.InternalServerError, "text/plain", ex.ToString());
			return;
		}

		System.Net.WebSockets.WebSocket ws = System.Net.WebSockets.WebSocket.CreateClientWebSocket(
			stream,
			request.Headers.GetValues("Sec-WebSocket-Protocol").FirstOrDefault() ?? string.Empty,
			1024,
			1024,
			TimeSpan.FromSeconds(30),
			true,
			System.Net.WebSockets.WebSocket.CreateClientBuffer(1024, 1024)
		);
		using MyWebSocket webSocket = new(client);
		await WebSocketHandler!(request, webSocket);
	}

	private async Task<HttpResponse?> CheckAndProcessWebSocketAsync(HttpRequest request)
	{
		bool hasUpgradeHeader = request.Headers.GetValues("Upgrade").Contains("websocket");
		bool hasConnectionHeader = request.Headers.GetValues("Connection").Contains("Upgrade");
		bool hasWebSocketVersion = request.Headers.GetValues("Sec-WebSocket-Version").Any();
		bool hasWebSocketKey = request.Headers.GetValues("Sec-WebSocket-Key").Any();

		if (!hasUpgradeHeader && !hasConnectionHeader && !hasWebSocketVersion && !hasWebSocketKey)
			return null;
		if (!hasUpgradeHeader || !hasConnectionHeader || !hasWebSocketVersion || !hasWebSocketKey)
			return new(HttpStatusCode.BadRequest, "text/plain", [], "Bad Request (invalid WebSocket request)");
		if (request.Version != "HTTP/1.1")
			return new(HttpStatusCode.BadRequest, "text/plain", [], "Bad Request (invalid HTTP version for WebSocket request)");
		if (request.Method != "GET")
			return new(HttpStatusCode.MethodNotAllowed, "text/plain", [], "Method Not Allowed (WebSocket request must be GET)");
		if (request.Headers.GetValues("Sec-WebSocket-Version").Length != 1)
			return new(HttpStatusCode.BadRequest, "text/plain", [], "Bad Request (invalid WebSocket version)");
		if (request.Headers.GetValues("Sec-WebSocket-Version")[0] != "13")
			return new(HttpStatusCode.BadRequest, "text/plain", [], "Bad Request (invalid WebSocket version)");
		if (request.Headers.GetValues("Sec-WebSocket-Key").Length != 1)
			return new(HttpStatusCode.BadRequest, "text/plain", [], "Bad Request (invalid WebSocket key)");
		if (request.Headers.GetValues("Sec-WebSocket-Key")[0].Length != 24)
			return new(HttpStatusCode.BadRequest, "text/plain", [], "Bad Request (invalid WebSocket key)");
		if (CanUpgradeToWebSocket is null || WebSocketHandler is null)
			return new(HttpStatusCode.NotImplemented, "text/plain", [], "Not Implemented (WebSocket is not supported)");

		HttpResponse acceptWebSocketResponse = new(HttpStatusCode.SwitchingProtocols, "text/plain", [], string.Empty);
		acceptWebSocketResponse.AdditionalHeaders.Add("Upgrade", "websocket");
		acceptWebSocketResponse.AdditionalHeaders.Add("Connection", "Upgrade");
		string secWebSocketAccept = Convert.ToBase64String(SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(request.Headers.GetValues("Sec-WebSocket-Key").First() + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
		acceptWebSocketResponse.AdditionalHeaders.Add("Sec-WebSocket-Accept", secWebSocketAccept);
		acceptWebSocketResponse.Version = "HTTP/1.1";

		return await CanUpgradeToWebSocket(request, acceptWebSocketResponse);
	}

	private Task WriteResponseAsync(HttpStatusCode statusCode, string contentType, string content, bool isHead = false)
		=> WriteResponseAsync($"{(int)statusCode} {HttpResponse.GetHttpStatusCodeDescription(statusCode)}", contentType, Encoding.UTF8.GetBytes(content), isHead);

	private Task WriteResponseAsync(string status, string contentType, string content, bool isHead = false)
		=> WriteResponseAsync(status, contentType, Encoding.UTF8.GetBytes(content), isHead);

	private Task WriteResponseAsync(string status, string contentType, byte[] content, bool isHead = false)
	{
		string headerStr = string.Join(crlfStr, [
			$"HTTP/1.0 {status}",
			$"Server: {typeof(HttpServer).FullName}",
			$"Content-Type: {contentType}; charset=UTF-8",
			$"Content-Length: {content.Length}",
			$"Date: {DateTime.UtcNow:R}",
			$"Connection: close",
			""
		]);

		return WriteResponseAsync(Encoding.UTF8.GetBytes(headerStr), content, isHead);
	}

	private static readonly string crlfStr = "\r\n";
	private static readonly byte[] crlf = [(byte)'\r', (byte)'\n'];
	private async Task WriteResponseAsync(byte[] header, byte[] content, bool isHead = false)
	{
		await stream.WriteAsync(header, 0, header.Length, cancellationToken);
		await stream.WriteAsync(crlf, 0, crlf.Length, cancellationToken);
		if (!isHead && 0 < content.Length)
			await stream.WriteAsync(content, 0, content.Length, cancellationToken);
	}

	private Task WriteResponseAsync(HttpResponse response, bool isHead = false)
	{
		List<string> commonHeaders = [
			$"{response.Version} {response.Status}",
			$"Server: {typeof(HttpServer).FullName}",
			$"Content-Type: {response.ContentType}; charset=UTF-8",
			$"Content-Length: {response.Body.Length}",
			$"Date: {DateTime.UtcNow:R}",
		];
		if (response.StatusCode != HttpStatusCode.SwitchingProtocols)
			commonHeaders.Add("Connection: close");

		string headerStr = string.Join(
			crlfStr,
			commonHeaders
				.Concat(response.AdditionalHeaders.AllKeys
					.Select(key => $"{key}: {response.AdditionalHeaders[key]}"))
				.Concat([""])
		);

		return WriteResponseAsync(Encoding.UTF8.GetBytes(headerStr), response.Body, isHead);
	}

	#region IDisposable Support
	private bool disposedValue;
	protected virtual void Dispose(bool disposing)
	{
		if (!disposedValue)
		{
			if (disposing)
			{
				stream.Dispose();
			}

			disposedValue = true;
		}
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
	#endregion IDisposable Support
}
