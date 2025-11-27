namespace TR.SimpleHttpServer.WebSocket;

/// <summary>
/// WebSocket frame opcode definitions (RFC 6455)
/// </summary>
public enum WebSocketOpcode : byte
{
	/// <summary>
	/// Continuation frame (0x0)
	/// </summary>
	Continuation = 0x0,

	/// <summary>
	/// Text frame (0x1)
	/// </summary>
	Text = 0x1,

	/// <summary>
	/// Binary frame (0x2)
	/// </summary>
	Binary = 0x2,

	/// <summary>
	/// Connection close frame (0x8)
	/// </summary>
	Close = 0x8,

	/// <summary>
	/// Ping frame (0x9)
	/// </summary>
	Ping = 0x9,

	/// <summary>
	/// Pong frame (0xA)
	/// </summary>
	Pong = 0xA
}
