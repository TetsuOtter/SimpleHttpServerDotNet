# E2E Tests

## Requirements

- Python 3.8+
- websockets library
- pytest
- pytest-asyncio

## Installation

```bash
pip install websockets pytest pytest-asyncio
```

## Running Tests

### 1. Start the Server

First, you need to run a WebSocket-enabled server. Modify `TR.SimpleHttpServer.Host/Program.cs` to include a WebSocket handler:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using TR.SimpleHttpServer;
using TR.SimpleHttpServer.WebSocket;

class Program : IDisposable
{
    static void Main(string[] args)
    {
        using Program program = new();
        program.Start();
        Console.WriteLine($"Server is running on port {program.server.Port}.");
        Console.WriteLine("Press any key to stop server.");
        Console.ReadKey();
    }

    readonly HttpServer server;

    public Program()
    {
        server = new HttpServer(8080, HandleRequest, HandleWebSocket);
    }

    public void Start() => server.Start();

    static Task<HttpResponse> HandleRequest(HttpRequest request)
    {
        HttpResponse response = new("200 OK", "text/plain", [], 
            $"Hello, World!\nThank you for requesting {request.Path} with method {request.Method}!");
        return Task.FromResult(response);
    }

    static async Task HandleWebSocket(HttpRequest request, WebSocketConnection connection)
    {
        while (connection.IsOpen)
        {
            try
            {
                var message = await connection.ReceiveMessageAsync(CancellationToken.None);
                
                if (message.Type == WebSocketMessageType.Close)
                {
                    await connection.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    break;
                }

                if (message.Type == WebSocketMessageType.Text)
                {
                    await connection.SendTextAsync($"Echo: {message.GetText()}", CancellationToken.None);
                }
                else if (message.Type == WebSocketMessageType.Binary)
                {
                    await connection.SendBinaryAsync(message.Data, CancellationToken.None);
                }
            }
            catch
            {
                break;
            }
        }
    }

    public void Dispose() => ((IDisposable)server).Dispose();
}
```

Then run:
```bash
dotnet run --project TR.SimpleHttpServer.Host
```

### 2. Run the Tests

```bash
cd e2e-tests
pytest -v
```

## Test Coverage

The E2E tests cover:
- WebSocket handshake
- Text message sending/receiving
- Binary message sending/receiving
- Multiple messages in sequence
- Close handshake
- Ping/pong
- Unicode messages
- Large messages
