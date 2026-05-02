# SimpleHttpServerDotNet

A simple and lightweight HTTP server library for .NET. Built on .NET Standard 2.0+ and provides an easy-to-use framework for handling HTTP requests. Full WebSocket support is included.

> 🇯🇵 [日本語版](./README.md)

## Features

- ✅ **Simple API**: Easy to build HTTP servers
- ✅ **Async/Await**: Task-based asynchronous API for high-efficiency processing
- ✅ **WebSocket Support**: RFC 6455-compliant native WebSocket communication
- ✅ **Protocol Validation**: Strict RFC 6455 validation of RSV bits, opcodes, masking, frame sizes, UTF-8 text payloads, and close codes
- ✅ **Ping/Pong**: Automatic pong replies to incoming pings with event notifications
- ✅ **Subprotocol Negotiation**: `Sec-WebSocket-Protocol` header negotiation
- ✅ **.NET Standard 2.0**: Compatible with netstandard2.1, net8.0, net10.0, and more
- ✅ **MIT License**: Free to use and modify

## Project Structure

### TR.SimpleHttpServer

The main library providing HTTP server functionality and WebSocket support.

**Main Classes:**

- `HttpServer`: The primary HTTP server class
- `HttpRequest`: Represents an HTTP request
- `HttpResponse`: Represents an HTTP response
- `WebSocketConnection`: Manages WebSocket connections
- `WebSocketOptions`: Configuration options for WebSocket behavior
- `WebSocketHandler`: Delegate for WebSocket processing
- `WebSocketProtocolException`: Exception thrown on WebSocket protocol violations

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
		return HandleWebSocketConnection;
	return null;
}

// Define WebSocket connection handler
async Task HandleWebSocketConnection(HttpRequest request, WebSocketConnection connection)
{
	while (connection.IsOpen)
	{
		var message = await connection.ReceiveMessageAsync(CancellationToken.None);

		if (message.Type == WebSocketMessageType.Close)
			break; // Close frame already sent automatically (AutoRespondToClose = true by default)

		if (message.Type == WebSocketMessageType.Text)
		{
			string text = message.GetText();
			await connection.SendTextAsync($"Echo: {text}", CancellationToken.None);
		}
	}
}

// Create and start server
using var server = new HttpServer(8080, HandleRequest, HandleWebSocketPath);
server.Start();
```

### Server with WebSocket Options

```csharp
using TR.SimpleHttpServer.WebSocket;

WebSocketOptions options = new()
{
	SupportedSubProtocols = new[] { "chat", "superchat" },
	MaxFrameSize = 4 * 1024 * 1024,    // 4 MB
	MaxMessageSize = 16 * 1024 * 1024, // 16 MB
	ValidateUtf8 = true,
};

using var server = new HttpServer(8080, HandleRequest, HandleWebSocketPath, options);
server.Start();
```

## API Reference

### HttpServer

```csharp
// Initialize HTTP server
public HttpServer(ushort port, HttpConnectionHandler handler);
public HttpServer(ushort port, HttpConnectionHandler handler, WebSocketHandlerSelector webSocketHandlerSelector);
public HttpServer(ushort port, HttpConnectionHandler handler, WebSocketHandlerSelector webSocketHandlerSelector, WebSocketOptions webSocketOptions);
public HttpServer(IPAddress localAddress, ushort port, HttpConnectionHandler handler, WebSocketHandlerSelector? webSocketHandlerSelector = null, WebSocketOptions? webSocketOptions = null);

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
// Create response with HttpStatusCode and string body
public HttpResponse(HttpStatusCode status, string contentType, NameValueCollection additionalHeaders, string body);

// Create response with status string and string body (e.g. "404 Not Found")
public HttpResponse(string status, string contentType, NameValueCollection additionalHeaders, string body);

// Create response with status string and binary body
public HttpResponse(string status, string contentType, NameValueCollection additionalHeaders, byte[] body);

public string Status { get; }
public string ContentType { get; }
public byte[] Body { get; }
public NameValueCollection AdditionalHeaders { get; }
```

### WebSocketOptions

```csharp
public class WebSocketOptions
{
	// Maximum payload size for a single frame in bytes (default: 16 MB)
	// Exceeding this closes the connection with 1009 (Message Too Big)
	public int MaxFrameSize { get; set; }

	// Maximum total payload size for a fragmented message in bytes (default: 64 MB)
	public int MaxMessageSize { get; set; }

	// Whether client frames must be masked (default: true)
	// RFC 6455 §5.1. Set to false for testing purposes only.
	public bool RequireMasking { get; set; }

	// Whether to automatically reply to a received Close frame (default: true)
	public bool AutoRespondToClose { get; set; }

	// Whether to validate UTF-8 in Text messages (default: true)
	// Invalid UTF-8 closes the connection with 1007 (Invalid Payload Data)
	public bool ValidateUtf8 { get; set; }

	// Subprotocols this server supports, in priority order.
	// null or empty means no subprotocol negotiation is performed.
	public IReadOnlyList<string>? SupportedSubProtocols { get; set; }
}
```

### WebSocketConnection

```csharp
// Constructors
public WebSocketConnection(Stream stream);
public WebSocketConnection(Stream stream, WebSocketOptions options, string? selectedSubProtocol = null);

// Check if connection is open
public bool IsOpen { get; }

// Subprotocol negotiated during handshake, or null if none
public string? SelectedSubProtocol { get; }

// Fired when a ping frame is received (after the automatic pong reply)
public event EventHandler<PingPongEventArgs>? PingReceived;

// Fired when a pong frame is received
public event EventHandler<PingPongEventArgs>? PongReceived;

// Receive a WebSocket message
// Throws WebSocketProtocolException on protocol violations (connection is already closed before throw)
public Task<WebSocketMessage> ReceiveMessageAsync(CancellationToken cancellationToken);

// Send a text message
public Task SendTextAsync(string text, CancellationToken cancellationToken);

// Send a binary message
public Task SendBinaryAsync(byte[] data, CancellationToken cancellationToken);

// Send a ping frame (payload must not exceed 125 bytes)
public Task SendPingAsync(byte[] data, CancellationToken cancellationToken);

// Send a close frame and close the connection
public Task CloseAsync(WebSocketCloseStatus status, string reason, CancellationToken cancellationToken);
```

### WebSocketMessage

```csharp
public class WebSocketMessage
{
	// Message type (Text / Binary / Close)
	public WebSocketMessageType Type { get; }

	// Raw message payload
	public byte[] Data { get; }

	// Close status code (null for non-Close messages)
	public WebSocketCloseStatus? CloseStatus { get; }

	// Close reason string (empty for non-Close messages)
	public string CloseReason { get; }

	// Get message data as a UTF-8 string
	public string GetText();
}
```

### WebSocketProtocolException

Thrown when an RFC 6455 protocol violation is detected. The connection is closed with the appropriate status code before this exception is raised.

```csharp
public class WebSocketProtocolException : Exception
{
	// The close status code sent to the peer
	public WebSocketCloseStatus CloseStatus { get; }
}
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

```bash
dotnet test TR.SimpleHttpServer.Tests
```

## Supported Frameworks

The library targets **netstandard2.0** and is compatible with:

- .NET Standard 2.0 / 2.1
- .NET 5 and later (net8.0, net10.0, etc.)
- .NET Framework 4.6.1 and later

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
