using System;
using System.Threading;
using System.Threading.Tasks;

using TR.SimpleHttpServer.WebSocket;

namespace TR.SimpleHttpServer.Host;

class Program : IDisposable
{
	static void Main(string[] args)
	{
		try
		{
			using Program program = new();

			program.Start();
			Console.WriteLine($"Server is running on port {program.server.Port}.");
			Console.WriteLine("WebSocket endpoint: ws://127.0.0.1:{0}/ws", program.server.Port);

			if (Console.IsInputRedirected)
			{
				Thread.Sleep(Timeout.Infinite);
			}
			else
			{
				Console.WriteLine("Press any key to stop server.");
				Console.ReadKey();
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine(ex);
		}
	}

	readonly HttpServer server;

	public Program()
	{
		server = new HttpServer(8080, HandleRequest, HandleWebSocket);
	}

	public void Start() => server.Start();

	static Task<HttpResponse> HandleRequest(HttpRequest request)
	{
		HttpResponse response = new("200 OK", "text/plain", [], $"Hello, World!\nThank you for requesting {request.Path} with method {request.Method}!");
		return Task.FromResult(response);
	}

	static async Task HandleWebSocket(HttpRequest request, WebSocketConnection connection)
	{
		Console.WriteLine($"WebSocket connection opened for {request.Path}");

		while (connection.IsOpen)
		{
			try
			{
				var message = await connection.ReceiveMessageAsync(CancellationToken.None);

				if (message.Type == WebSocketMessageType.Close)
				{
					Console.WriteLine("WebSocket close received");
					await connection.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
					break;
				}

				if (message.Type == WebSocketMessageType.Text)
				{
					string text = message.GetText();
					Console.WriteLine($"WebSocket text received: {text}");
					await connection.SendTextAsync($"Echo: {text}", CancellationToken.None);
				}
				else if (message.Type == WebSocketMessageType.Binary)
				{
					Console.WriteLine($"WebSocket binary received: {message.Data.Length} bytes");
					await connection.SendBinaryAsync(message.Data, CancellationToken.None);
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"WebSocket error: {ex.Message}");
				break;
			}
		}

		Console.WriteLine("WebSocket connection closed");
	}

	public void Dispose() => ((IDisposable)server).Dispose();
}
