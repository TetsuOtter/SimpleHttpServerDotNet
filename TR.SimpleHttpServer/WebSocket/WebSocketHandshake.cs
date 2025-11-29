using System;
using System.Collections.Specialized;
using System.Security.Cryptography;
using System.Text;

namespace TR.SimpleHttpServer.WebSocket;

/// <summary>
/// Handles WebSocket handshake processing
/// </summary>
public static class WebSocketHandshake
{
	/// <summary>
	/// WebSocket GUID used for handshake (RFC 6455)
	/// </summary>
	private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

	/// <summary>
	/// Checks if the HTTP request is a WebSocket upgrade request
	/// </summary>
	public static bool IsWebSocketUpgradeRequest(HttpRequest request)
	{
		if (request == null)
			return false;

		// Must be GET method
		if (request.Method != "GET")
			return false;

		// Check Connection header contains "upgrade"
		string connection = request.Headers["Connection"] ?? "";
		if (!ContainsIgnoreCase(connection, "upgrade"))
			return false;

		// Check Upgrade header is "websocket"
		string upgrade = request.Headers["Upgrade"] ?? "";
		if (!string.Equals(upgrade.Trim(), "websocket", StringComparison.OrdinalIgnoreCase))
			return false;

		// Check Sec-WebSocket-Key header exists
		string key = request.Headers["Sec-WebSocket-Key"];
		if (string.IsNullOrEmpty(key))
			return false;

		// Check Sec-WebSocket-Version header
		string version = request.Headers["Sec-WebSocket-Version"];
		if (version != "13")
			return false;

		return true;
	}

	/// <summary>
	/// Creates the Sec-WebSocket-Accept value for the handshake response
	/// </summary>
	public static string ComputeAcceptKey(string secWebSocketKey)
	{
		if (string.IsNullOrEmpty(secWebSocketKey))
			throw new ArgumentNullException(nameof(secWebSocketKey));

		string combined = secWebSocketKey.Trim() + WebSocketGuid;
		using (SHA1 sha1 = SHA1.Create())
		{
			byte[] hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(combined));
			return Convert.ToBase64String(hashBytes);
		}
	}

	/// <summary>
	/// Creates the response headers for a successful WebSocket upgrade
	/// </summary>
	public static NameValueCollection CreateUpgradeResponseHeaders(string secWebSocketKey)
	{
		NameValueCollection headers = new()
		{
			{ "Upgrade", "websocket" },
			{ "Connection", "Upgrade" },
			{ "Sec-WebSocket-Accept", ComputeAcceptKey(secWebSocketKey) }
		};

		return headers;
	}

	/// <summary>
	/// Validates and extracts WebSocket key from request
	/// </summary>
	public static string GetSecWebSocketKey(HttpRequest request)
	{
		return request?.Headers["Sec-WebSocket-Key"] ?? "";
	}

	private static bool ContainsIgnoreCase(string source, string value)
	{
		if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value))
			return false;

		// Split by comma and check each part
		string[] parts = source.Split(',');
		foreach (string part in parts)
		{
			if (string.Equals(part.Trim(), value, StringComparison.OrdinalIgnoreCase))
				return true;
		}
		return false;
	}
}
