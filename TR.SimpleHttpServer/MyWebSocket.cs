using System;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace TR.SimpleHttpServer;

public class MyWebSocket(
	TcpClient client
) : WebSocket
{
	readonly TcpClient client = client;

	public override WebSocketCloseStatus? CloseStatus => throw new NotImplementedException();

	public override string CloseStatusDescription => throw new NotImplementedException();

	public override WebSocketState State => throw new NotImplementedException();

	public override string SubProtocol => throw new NotImplementedException();

	public override void Abort() => throw new NotImplementedException();
	public override Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken) => throw new NotImplementedException();
	public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken) => throw new NotImplementedException();
	public override void Dispose() => throw new NotImplementedException();
	public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) => throw new NotImplementedException();
	public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) => throw new NotImplementedException();
}
