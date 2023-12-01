using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace TR.SimpleHttpServer;

internal class ProcessOneConnectionWorker
(
	TcpClient client,
	CancellationToken cancellationToken,
	HttpConnectionHandler handler
) : IDisposable
{
	private readonly TcpClient client = client;
	private readonly NetworkStream stream = client.GetStream();
	private readonly CancellationToken cancellationToken = cancellationToken;
	private readonly HttpConnectionHandler handler = handler;

	public async Task ProcessAsync()
	{
		if (disposedValue)
			throw new ObjectDisposedException(nameof(ProcessOneConnectionWorker));

		if (!client.Connected)
			throw new InvalidOperationException("Client is not connected.");

		cancellationToken.ThrowIfCancellationRequested();

		stream.ReadTimeout = 2000;
		stream.WriteTimeout = 2000;
		using StreamReader reader = new(stream);
		await ProcessAsync(reader);
		await stream.FlushAsync(cancellationToken);
	}

	private async Task ProcessAsync(StreamReader reader)
	{
		bool isHeadMethod;
		string method;
		string rawPath;
		string httpVersion;
		byte[] body = [];
		NameValueCollection headers = [];
		string requestLine = await reader.ReadLineAsync();
		if (requestLine is null)
		{
			await WriteResponseAsync("400 Bad Request", "text/plain", "Bad Request (empty request)");
			return;
		}

		{
			string[] requestLineParts = requestLine.Split([' '], 3);
			if (requestLineParts.Length != 3)
			{
				await WriteResponseAsync("400 Bad Request", "text/plain", "Bad Request (invalid request line)");
				return;
			}

			method = requestLineParts[0].ToUpper();
			rawPath = requestLineParts[1];
			httpVersion = requestLineParts[2];
			isHeadMethod = method == "HEAD";
		}

		while (true)
		{
			string? headerLine = await reader.ReadLineAsync();
			if (headerLine is null)
			{
				await WriteResponseAsync("400 Bad Request", "text/plain", "Bad Request (empty header)");
				return;
			}

			if (headerLine == "")
				break;

			string[] headerParts = headerLine.Split([':'], 2);
			if (headerParts.Length != 2)
			{
				await WriteResponseAsync("400 Bad Request", "text/plain", "Bad Request (invalid header)");
				return;
			}

			headers.Add(headerParts[0], headerParts[1].Trim());
		}

		if (headers.GetValues("Content-Length") is string[] contentLengthValues)
		{
			if (contentLengthValues.Length != 1)
			{
				await WriteResponseAsync("400 Bad Request", "text/plain", "Bad Request (invalid Content-Length header)");
				return;
			}

			if (!long.TryParse(contentLengthValues[0], out long contentLength))
			{
				await WriteResponseAsync("400 Bad Request", "text/plain", "Bad Request (invalid Content-Length header)");
				return;
			}

			if (0 < contentLength)
			{
				body = new byte[contentLength];
				await reader.BaseStream.ReadAsync(body, 0, body.Length);
			}
		}

		Uri uri = new(rawPath);
		string path = uri.LocalPath;
		string query = "";
		int questionMarkIndex = path.IndexOf('?');
		if (questionMarkIndex != -1)
		{
			query = path.Substring(questionMarkIndex + 1);
			path = path.Substring(0, questionMarkIndex);
		}

		NameValueCollection queryString = HttpUtility.ParseQueryString(query);
		HttpRequest request = new(method, uri.LocalPath, headers, queryString, body);
		try
		{
			HttpResponse response = await handler(request);
			await WriteResponseAsync(response, hasBody: !isHeadMethod);
		}
		catch (Exception ex)
		{
			await WriteResponseAsync("500 Internal Server Error", "text/plain", ex.ToString());
			return;
		}
	}

	private Task WriteResponseAsync(string status, string contentType, string content, bool isHead = false)
		=> WriteResponseAsync(status, contentType, Encoding.UTF8.GetBytes(content), isHead);

	private Task WriteResponseAsync(string status, string contentType, byte[] content, bool hasBody = false)
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

		return WriteResponseAsync(Encoding.UTF8.GetBytes(headerStr), content, hasBody);
	}

	private static readonly string crlfStr = "\r\n";
	private static readonly byte[] crlf = [(byte)'\r', (byte)'\n'];
	private async Task WriteResponseAsync(byte[] header, byte[] content, bool hasBody = false)
	{
		await stream.WriteAsync(header, 0, header.Length, cancellationToken);
		await stream.WriteAsync(crlf, 0, crlf.Length, cancellationToken);
		if (hasBody && 0 < content.Length)
			await stream.WriteAsync(content, 0, content.Length, cancellationToken);
	}

	private Task WriteResponseAsync(HttpResponse response, bool hasBody = false)
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

		return WriteResponseAsync(Encoding.UTF8.GetBytes(headerStr), response.Body, hasBody);
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
