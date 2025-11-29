using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
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
			Console.WriteLine("HTTP endpoint: http://127.0.0.1:{0}/", program.server.Port);
			Console.WriteLine("WebSocket echo endpoint: ws://127.0.0.1:{0}/ws", program.server.Port);
			Console.WriteLine("WebSocket chat endpoint: ws://127.0.0.1:{0}/chat-ws", program.server.Port);

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
	static readonly ConcurrentDictionary<string, (WebSocketConnection Connection, string Name)> chatClients = new();

	public Program()
	{
		server = new HttpServer(8080, HandleRequest, HandleWebSocketPath);
	}

	public void Start() => server.Start();

	static Task<HttpResponse> HandleRequest(HttpRequest request)
	{
		// Serve embedded resources based on path
		string path = request.Path;

		// Route to appropriate HTML page
		if (path == "/" || path == "/index.html")
		{
			return ServeEmbeddedResource("index.html", "text/html");
		}
		else if (path == "/paths" || path == "/paths.html")
		{
			return ServeEmbeddedResource("paths.html", "text/html");
		}
		else if (path == "/chat" || path == "/chat.html")
		{
			return ServeEmbeddedResource("chat.html", "text/html");
		}

		// Default response for other paths
		HttpResponse response = new(HttpStatusCode.OK, "text/plain", [], $"Hello, World!\nThank you for requesting {request.Path} with method {request.Method}!");
		return Task.FromResult(response);
	}

	static Task<HttpResponse> ServeEmbeddedResource(string resourceName, string contentType)
	{
		var assembly = Assembly.GetExecutingAssembly();
		var fullResourceName = $"TR.SimpleHttpServer.Host.Resources.{resourceName}";

		using var stream = assembly.GetManifestResourceStream(fullResourceName);
		if (stream == null)
		{
			HttpResponse notFound = new(HttpStatusCode.NotFound, "text/plain", [], $"Resource not found: {resourceName}");
			return Task.FromResult(notFound);
		}

		using var reader = new StreamReader(stream, Encoding.UTF8);
		string content = reader.ReadToEnd();

		HttpResponse response = new(HttpStatusCode.OK, contentType, [], content);
		return Task.FromResult(response);
	}

	static async Task<WebSocketHandler?> HandleWebSocketPath(string path)
	{
		// Handle /ws path for echo
		if (path == "/ws")
		{
			return HandleWebSocketEcho;
		}
		// Handle /chat-ws path for chat
		if (path == "/chat-ws")
		{
			return HandleWebSocketChat;
		}
		return null;
	}

	static async Task HandleWebSocketEcho(HttpRequest request, WebSocketConnection connection)
	{
		Console.WriteLine($"WebSocket echo connection opened for {request.Path}");

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

		Console.WriteLine("WebSocket echo connection closed");
	}

	static async Task HandleWebSocketChat(HttpRequest request, WebSocketConnection connection)
	{
		string clientId = Guid.NewGuid().ToString();
		string clientName = "";
		Console.WriteLine($"WebSocket chat connection opened: {clientId}");

		try
		{
			while (connection.IsOpen)
			{
				var message = await connection.ReceiveMessageAsync(CancellationToken.None);

				if (message.Type == WebSocketMessageType.Close)
				{
					Console.WriteLine($"WebSocket chat close received from {clientName}");
					break;
				}

				if (message.Type == WebSocketMessageType.Text)
				{
					string text = message.GetText();
					ChatMessage? chatMessage;
					try
					{
						chatMessage = JsonSerializer.Deserialize<ChatMessage>(text);
					}
					catch
					{
						continue;
					}

					if (chatMessage == null) continue;

					if (chatMessage.type == "join")
					{
						clientName = chatMessage.name ?? "Anonymous";
						chatClients[clientId] = (connection, clientName);
						Console.WriteLine($"Chat user joined: {clientName}");
						await BroadcastMessage(new ChatMessage { type = "join", name = clientName });
					}
					else if (chatMessage.type == "chat")
					{
						Console.WriteLine($"Chat message from {clientName}: {chatMessage.message}");
						await BroadcastMessage(new ChatMessage { type = "chat", name = clientName, message = chatMessage.message });
					}
					else if (chatMessage.type == "leave")
					{
						Console.WriteLine($"Chat user leaving: {clientName}");
						break;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"WebSocket chat error: {ex.Message}");
		}
		finally
		{
			// Remove client and broadcast leave message
			if (chatClients.TryRemove(clientId, out _))
			{
				if (!string.IsNullOrEmpty(clientName))
				{
					Console.WriteLine($"Chat user left: {clientName}");
					await BroadcastMessage(new ChatMessage { type = "leave", name = clientName });
				}
			}

			try
			{
				if (connection.IsOpen)
				{
					await connection.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
				}
			}
			catch
			{
				// Ignore close errors
			}
		}

		Console.WriteLine($"WebSocket chat connection closed: {clientId}");
	}

	static async Task BroadcastMessage(ChatMessage message)
	{
		string json = JsonSerializer.Serialize(message);

		foreach (var kvp in chatClients)
		{
			try
			{
				if (kvp.Value.Connection.IsOpen)
				{
					await kvp.Value.Connection.SendTextAsync(json, CancellationToken.None);
				}
				else
				{
					// Remove disconnected client
					chatClients.TryRemove(kvp.Key, out _);
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error broadcasting to {kvp.Value.Name}: {ex.Message}");
				// Remove failed client
				chatClients.TryRemove(kvp.Key, out _);
			}
		}
	}

	public void Dispose() => ((IDisposable)server).Dispose();
}

class ChatMessage
{
	public string? type { get; set; }
	public string? name { get; set; }
	public string? message { get; set; }
}
