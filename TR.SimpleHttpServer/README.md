# TR.SimpleHttpServer

A lightweight, easy-to-use HTTP server library for .NET. Build HTTP and WebSocket servers with minimal code.

## Features

✨ **Simple API** - Get your server running in just a few lines of code
⚡ **Async/Await** - Full async support for high-performance applications
🔌 **WebSocket Ready** - Native WebSocket communication support
📦 **Cross-platform** - .NET Standard 2.0+ compatible
✅ **Production Ready** - Fully tested and documented

## Quick Start

### Basic HTTP Server

```csharp
using TR.SimpleHttpServer;
using System.Net;

async Task<HttpResponse> HandleRequest(HttpRequest request)
{
	return new HttpResponse(
		HttpStatusCode.OK,
		"text/plain",
		new System.Collections.Specialized.NameValueCollection(),
		"Hello, World!"
	);
}

using var server = new HttpServer(8080, HandleRequest);
server.Start();

// Server is now running on http://localhost:8080/
Console.ReadLine(); // Keep server running
```

### WebSocket Server

```csharp
using TR.SimpleHttpServer;
using TR.SimpleHttpServer.WebSocket;

async Task<WebSocketHandler?> SelectWebSocketHandler(string path)
{
	return path == "/ws" ? HandleWebSocket : null;
}

async Task HandleWebSocket(HttpRequest request, WebSocketConnection connection)
{
	while (connection.IsOpen)
	{
		var message = await connection.ReceiveMessageAsync(CancellationToken.None);

		if (message.Type == WebSocketMessageType.Text)
		{
			await connection.SendTextAsync($"Echo: {message.GetText()}", CancellationToken.None);
		}
	}
}

using var server = new HttpServer(8080, HandleRequest, SelectWebSocketHandler);
server.Start();
```

## Supported Frameworks

- .NET Standard 2.0

## API Overview

### HttpServer

Main class for running an HTTP server with optional WebSocket support.

- `Start()` - Start the server
- `Stop()` - Stop the server
- `IsRunning` - Check if server is running
- `Port` - Get the listening port

### HttpRequest

Represents an incoming HTTP request.

- `Method` - HTTP method (GET, POST, etc.)
- `Path` - Request path
- `Headers` - HTTP headers
- `QueryString` - Query parameters
- `Body` - Request body as byte array

### HttpResponse

Represents an HTTP response to send back.

- Status code
- Content-Type
- Custom headers
- Body (string or byte array)

### WebSocket Classes

Complete WebSocket support for real-time bidirectional communication:

- `WebSocketConnection` - Manage WebSocket connections
- `WebSocketMessage` - Handle WebSocket messages
- `WebSocketHandler` - Process WebSocket events

## Documentation

For more examples and detailed documentation, visit the [GitHub repository](https://github.com/TetsuOtter/SimpleHttpServerDotNet).

## License

MIT License - See LICENSE file for details

## Support

- 🐛 [Report Issues](https://github.com/TetsuOtter/SimpleHttpServerDotNet/issues)
- 💡 [Feature Requests](https://github.com/TetsuOtter/SimpleHttpServerDotNet/issues)
- 📚 [Full Documentation](https://github.com/TetsuOtter/SimpleHttpServerDotNet/blob/main/README.en.md)
