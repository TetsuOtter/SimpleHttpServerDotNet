using System;
using System.Text;

namespace TR.SimpleHttpServer.WebSocket;

/// <summary>
/// Represents a WebSocket message
/// </summary>
public class WebSocketMessage
{
	/// <summary>
	/// Type of the message
	/// </summary>
	public WebSocketMessageType Type { get; }

	/// <summary>
	/// Raw message data
	/// </summary>
	public byte[] Data { get; }

	/// <summary>
	/// Close status code (only for Close messages)
	/// </summary>
	public WebSocketCloseStatus? CloseStatus { get; }

	/// <summary>
	/// Close reason (only for Close messages)
	/// </summary>
	public string CloseReason { get; }

	/// <summary>
	/// Creates a new WebSocket message
	/// </summary>
	public WebSocketMessage(WebSocketMessageType type, byte[] data, WebSocketCloseStatus? closeStatus = null, string closeReason = "")
	{
		Type = type;
		Data = data ?? throw new ArgumentNullException(nameof(data));
		CloseStatus = closeStatus;
		CloseReason = closeReason ?? "";
	}

	/// <summary>
	/// Gets the message data as a UTF-8 string
	/// </summary>
	public string GetText()
	{
		return Encoding.UTF8.GetString(Data);
	}
}
