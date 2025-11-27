namespace TR.SimpleHttpServer.WebSocket;

/// <summary>
/// WebSocket message types
/// </summary>
public enum WebSocketMessageType
{
	/// <summary>
	/// Text message
	/// </summary>
	Text,

	/// <summary>
	/// Binary message
	/// </summary>
	Binary,

	/// <summary>
	/// Connection close message
	/// </summary>
	Close
}
