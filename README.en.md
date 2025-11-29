# SimpleHttpServerDotNet

A simple and lightweight HTTP server library for .NET. Built on .NET Standard 2.0+ and provides an easy-to-use framework for handling HTTP requests. Full WebSocket support is included.

> 🇯🇵 [日本語版](./README.md)

## Features

- ✅ **Simple API**: Easy to build HTTP servers
- ✅ **Async/Await**: Task-based asynchronous API for high-efficiency processing
- ✅ **WebSocket Support**: Native WebSocket communication support
- ✅ **Multi-Framework Support**: Supports netstandard2.0, netstandard2.1, net8.0, and net10.0
- ✅ **MIT License**: Free to use and modify

## Project Structure

### TR.SimpleHttpServer

The main library providing HTTP server functionality and WebSocket support.

**Main Classes:**

- `HttpServer`: The primary HTTP server class
- `HttpRequest`: Represents an HTTP request
- `HttpResponse`: Represents an HTTP response
- `WebSocketConnection`: Manages WebSocket connections
- `WebSocketHandler`: Delegate for WebSocket processing

### TR.SimpleHttpServer.Host

A host application demonstrating library usage.

- HTTP endpoints (static file serving)
- WebSocket echo endpoint
- WebSocket chat application

### TR.SimpleHttpServer.Tests

Unit tests and WebSocket integration tests.

## Installation

### Using NuGet

```bash
dotnet add package TR.SimpleHttpServer
```

### Building from Source

```bash
git clone https://github.com/TetsuOtter/SimpleHttpServerDotNet.git
cd SimpleHttpServerDotNet
dotnet build TR.SimpleHttpServer.sln
```

## Quick Start

### Basic HTTP Server

```csharp
using TR.SimpleHttpServer;
using System.Net;

// Define request handler
async Task<HttpResponse> HandleRequest(HttpRequest request)
{
	return new HttpResponse(
		HttpStatusCode.OK,
		"text/plain",
		new System.Collections.Specialized.NameValueCollection(),
		$"Hello, {request.Path}!"
	);
}

// Create and start server
using var server = new HttpServer(8080, HandleRequest);
server.Start();

Console.WriteLine("Server is running on http://localhost:8080/");
Console.ReadKey();
```

### WebSocket-Enabled Server

```csharp
using TR.SimpleHttpServer;
using TR.SimpleHttpServer.WebSocket;

// Define WebSocket handler selector
async Task<WebSocketHandler?> HandleWebSocketPath(string path)
{
	if (path == "/ws")
	{
		return HandleWebSocketConnection;
	}
	return null;
}

// Define WebSocket connection handler
async Task HandleWebSocketConnection(HttpRequest request, WebSocketConnection connection)
{
	while (connection.IsOpen)
	{
		var message = await connection.ReceiveMessageAsync(CancellationToken.None);

		if (message.Type == WebSocketMessageType.Close)
		{
			await connection.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
			break;
		}

		if (message.Type == WebSocketMessageType.Text)
		{
			// Echo text message back
			string text = message.GetText();
			await connection.SendTextAsync($"Echo: {text}", CancellationToken.None);
		}
	}
}

// Create and start server
using var server = new HttpServer(8080, HandleRequest, HandleWebSocketPath);
server.Start();
```

## API Reference

### HttpServer

```csharp
// Initialize HTTP server
public HttpServer(ushort port, HttpConnectionHandler handler);
public HttpServer(ushort port, HttpConnectionHandler handler, WebSocketHandlerSelector webSocketHandlerSelector);
public HttpServer(IPAddress localAddress, ushort port, HttpConnectionHandler handler, WebSocketHandlerSelector? webSocketHandlerSelector = null);

// Start server
public void Start();

// Stop server
public void Stop();

// Check if server is running
public bool IsRunning { get; }

// Get bound port number
public ushort Port { get; }
```

### HttpRequest

```csharp
public class HttpRequest
{
	// HTTP method (GET, POST, etc.)
	public string Method { get; }

	// Request path
	public string Path { get; }

	// HTTP headers
	public NameValueCollection Headers { get; }

	// Query string parameters
	public NameValueCollection QueryString { get; }

	// Request body
	public byte[] Body { get; }
}
```

### HttpResponse

```csharp
// Create response with status code and string body
public HttpResponse(HttpStatusCode status, string contentType, NameValueCollection additionalHeaders, string body);

// Create response with status code and binary body
public HttpResponse(HttpStatusCode status, string contentType, NameValueCollection additionalHeaders, byte[] body);

public string Status { get; }
public string ContentType { get; }
public byte[] Body { get; }
public NameValueCollection AdditionalHeaders { get; }
```

### WebSocketConnection

```csharp
// Receive WebSocket message
public Task<WebSocketMessage> ReceiveMessageAsync(CancellationToken cancellationToken);

// Send text message
public Task SendTextAsync(string text, CancellationToken cancellationToken);

// Send binary message
public Task SendBinaryAsync(byte[] data, CancellationToken cancellationToken);

// Close WebSocket connection
public Task CloseAsync(WebSocketCloseStatus status, string statusDescription, CancellationToken cancellationToken);

// Check if connection is open
public bool IsOpen { get; }
```

## Host Application

The included `TR.SimpleHttpServer.Host` application provides the following endpoints:

```bash
dotnet run --project TR.SimpleHttpServer.Host
```

- **HTTP**: `http://localhost:8080/` - Index page
- **WebSocket echo**: `ws://localhost:8080/ws` - Echo messages back
- **WebSocket chat**: `ws://localhost:8080/chat-ws` - Multi-user chat

## Testing

### Running Unit Tests

```bash
dotnet test TR.SimpleHttpServer.Tests
```

### Running E2E Tests

WebSocket integration tests:

```bash
# Start the server
dotnet run --project TR.SimpleHttpServer.Host &

# Run tests
cd e2e-tests
pip install -r requirements.txt
pytest -v
```

## Supported Frameworks

- .NET Standard 2.0

## License

MIT License - See [LICENSE](LICENSE) for details

## Contributing

Bug reports and feature requests are welcome via Issues. Code improvements can be contributed via Pull Requests.

## Author

Tetsu Otter (Tech Otter)

## References

- [GitHub Repository](https://github.com/TetsuOtter/SimpleHttpServerDotNet)
- [WebSocket RFC 6455](https://tools.ietf.org/html/rfc6455)
- [HTTP/1.1 RFC 7230](https://tools.ietf.org/html/rfc7230)
