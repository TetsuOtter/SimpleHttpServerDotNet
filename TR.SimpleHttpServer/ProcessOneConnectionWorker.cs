using System;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

using TR.SimpleHttpServer.WebSocket;

namespace TR.SimpleHttpServer;

internal class ProcessOneConnectionWorker
(
	TcpClient client,
	CancellationToken cancellationToken,
	HttpConnectionHandler handler,
	WebSocketHandler? webSocketHandler
) : IDisposable
{
	private readonly TcpClient client = client;
	private readonly NetworkStream stream = client.GetStream();
	private readonly CancellationToken cancellationToken = cancellationToken;
	private readonly HttpConnectionHandler handler = handler;
	private readonly WebSocketHandler? webSocketHandler = webSocketHandler;

	/// <summary>
	/// Socket linger timeout in seconds for graceful connection closure
	/// </summary>
	private const int SocketLingerTimeoutSeconds = 5;

	public async Task ProcessAsync()
	{
		// Enable lingering to ensure data is transmitted before socket closes
		client.LingerState = new System.Net.Sockets.LingerOption(true, SocketLingerTimeoutSeconds);
		if (disposedValue)
			throw new ObjectDisposedException(nameof(ProcessOneConnectionWorker));

		if (!client.Connected)
			throw new InvalidOperationException("Client is not connected.");

		cancellationToken.ThrowIfCancellationRequested();

		stream.ReadTimeout = 2000;
		stream.WriteTimeout = 2000;
		MyStreamReader reader = new(stream, cancellationToken, Encoding.UTF8);
		await ProcessAsync(reader);
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
		HttpRequest request = new(method, path, headers, queryString, body);

		// Check for WebSocket upgrade request
		if (webSocketHandler != null && WebSocketHandshake.IsWebSocketUpgradeRequest(request))
		{
			try
			{
				await HandleWebSocketUpgradeAsync(request);
			}
			catch (Exception ex)
			{
				await WriteResponseAsync(HttpStatusCode.InternalServerError, "text/plain", ex.ToString());
			}
			return;
		}

		try
		{
			HttpResponse response = await handler(request);
			await WriteResponseAsync(response, isHead: isHeadMethod);
		}
		catch (Exception ex)
		{
			await WriteResponseAsync(HttpStatusCode.InternalServerError, "text/plain", ex.ToString());
			return;
		}
	}

	private async Task HandleWebSocketUpgradeAsync(HttpRequest request)
	{
		// Get the WebSocket key
		string secWebSocketKey = WebSocketHandshake.GetSecWebSocketKey(request);
		if (string.IsNullOrEmpty(secWebSocketKey))
		{
			await WriteResponseAsync(HttpStatusCode.BadRequest, "text/plain", "Missing Sec-WebSocket-Key header");
			return;
		}

		// Send the upgrade response
		await WriteWebSocketUpgradeResponseAsync(secWebSocketKey);

		// Remove timeouts for WebSocket connections (they can be long-lived)
		stream.ReadTimeout = System.Threading.Timeout.Infinite;
		stream.WriteTimeout = System.Threading.Timeout.Infinite;

		// Create WebSocket connection and invoke handler
		using (WebSocketConnection connection = new WebSocketConnection(stream))
		{
			try
			{
				await webSocketHandler!(request, connection);
			}
			catch (System.IO.EndOfStreamException)
			{
				// Client disconnected, this is expected
			}
		}

		// Ensure any buffered data is transmitted before closing
		try
		{
			await stream.FlushAsync(cancellationToken);
			// Gracefully shutdown the socket - this signals to the client that we're done sending
			// but allows remaining data to be transmitted
			client.Client.Shutdown(System.Net.Sockets.SocketShutdown.Send);
		}
		catch
		{
			// Ignore shutdown errors on close
		}
	}

	private async Task WriteWebSocketUpgradeResponseAsync(string secWebSocketKey)
	{
		NameValueCollection headers = WebSocketHandshake.CreateUpgradeResponseHeaders(secWebSocketKey);

		string headerStr = string.Join(crlfStr, new[] {
			"HTTP/1.1 101 Switching Protocols",
			$"Server: {typeof(HttpServer).FullName}",
			$"Date: {DateTime.UtcNow:R}",
			$"Upgrade: {headers["Upgrade"]}",
			$"Connection: {headers["Connection"]}",
			$"Sec-WebSocket-Accept: {headers["Sec-WebSocket-Accept"]}",
			""
		});

		byte[] responseBytes = Encoding.UTF8.GetBytes(headerStr);
		await stream.WriteAsync(responseBytes, 0, responseBytes.Length, cancellationToken);
		await stream.WriteAsync(crlf, 0, crlf.Length, cancellationToken);
		await stream.FlushAsync(cancellationToken);
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
		string[] commonHeaders = [
			$"HTTP/1.0 {response.Status}",
			$"Server: {typeof(HttpServer).FullName}",
			$"Content-Type: {response.ContentType}; charset=UTF-8",
			$"Content-Length: {response.Body.Length}",
			$"Date: {DateTime.UtcNow:R}",
			$"Connection: close"
		];

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
