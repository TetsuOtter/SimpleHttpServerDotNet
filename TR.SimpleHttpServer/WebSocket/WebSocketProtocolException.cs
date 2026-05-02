using System;

namespace TR.SimpleHttpServer.WebSocket;

/// <summary>
/// Thrown when a WebSocket protocol violation is detected on the receive side.
/// The connection is closed with the indicated <see cref="CloseStatus"/> before the exception is raised.
/// </summary>
public class WebSocketProtocolException : Exception
{
	/// <summary>
	/// The close status code sent to the peer before this exception was thrown.
	/// </summary>
	public WebSocketCloseStatus CloseStatus { get; }

	public WebSocketProtocolException(WebSocketCloseStatus closeStatus, string message)
		: base(message)
	{
		CloseStatus = closeStatus;
	}

	public WebSocketProtocolException(WebSocketCloseStatus closeStatus, string message, Exception innerException)
		: base(message, innerException)
	{
		CloseStatus = closeStatus;
	}
}
