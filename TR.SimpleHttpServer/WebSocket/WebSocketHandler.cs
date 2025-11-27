using System.Threading.Tasks;

namespace TR.SimpleHttpServer.WebSocket;

/// <summary>
/// Delegate for handling WebSocket connections
/// </summary>
/// <param name="request">The HTTP request that initiated the upgrade</param>
/// <param name="connection">The WebSocket connection</param>
/// <returns>A task that completes when the WebSocket handler is done</returns>
public delegate Task WebSocketHandler(HttpRequest request, WebSocketConnection connection);
