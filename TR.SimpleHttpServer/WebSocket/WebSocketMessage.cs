using System;
using System.Text;

namespace TR.SimpleHttpServer.WebSocket;

/// <summary>
/// Represents a WebSocket message
/// </summary>
public class WebSocketMessage(
	WebSocketMessageType type,
	byte[] data,
	WebSocketCloseStatus? closeStatus = null,
	string closeReason = ""
)
{
	/// <summary>
	/// Type of the message
	/// </summary>
	public WebSocketMessageType Type { get; } = type;

	/// <summary>
	/// Raw message data
	/// </summary>
	public byte[] Data { get; } = data ?? throw new ArgumentNullException(nameof(data));

	/// <summary>
	/// Close status code (only for Close messages)
	/// </summary>
	public WebSocketCloseStatus? CloseStatus { get; } = closeStatus;

	/// <summary>
	/// Close reason (only for Close messages)
	/// </summary>
	public string CloseReason { get; } = closeReason ?? "";

	/// <summary>
	/// Gets the message data as a UTF-8 string
	/// </summary>
	public string GetText()
	{
		return Encoding.UTF8.GetString(Data);
	}
}
