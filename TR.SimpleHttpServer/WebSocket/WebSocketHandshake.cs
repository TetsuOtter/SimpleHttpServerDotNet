using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;

namespace TR.SimpleHttpServer.WebSocket;

/// <summary>
/// Handles WebSocket handshake processing
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class WebSocketHandshake
{
	/// <summary>
	/// WebSocket GUID used for handshake (RFC 6455)
	/// </summary>
	private const string WEBSOCKET_GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

	/// <summary>
	/// Checks if the HTTP request is a WebSocket upgrade request
	/// </summary>
	public static bool IsWebSocketUpgradeRequest(HttpRequest request)
	{
		if (request is null)
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
	/// Returns true if the request looks like a WebSocket upgrade attempt with the wrong
	/// <c>Sec-WebSocket-Version</c> (RFC 6455 §4.4 requires the server to respond with 400 plus
	/// a <c>Sec-WebSocket-Version: 13</c> header so the client can retry).
	/// </summary>
	public static bool IsWebSocketVersionMismatch(HttpRequest request)
	{
		if (request is null) return false;
		if (request.Method != "GET") return false;
		string upgrade = request.Headers["Upgrade"] ?? "";
		if (!string.Equals(upgrade.Trim(), "websocket", StringComparison.OrdinalIgnoreCase)) return false;
		string connection = request.Headers["Connection"] ?? "";
		if (!ContainsIgnoreCase(connection, "upgrade")) return false;
		string version = request.Headers["Sec-WebSocket-Version"] ?? "";
		return !string.IsNullOrEmpty(version) && version != "13";
	}

	/// <summary>
	/// Creates the Sec-WebSocket-Accept value for the handshake response
	/// </summary>
	public static string ComputeAcceptKey(string secWebSocketKey)
	{
		if (string.IsNullOrEmpty(secWebSocketKey))
			throw new ArgumentNullException(nameof(secWebSocketKey));

		string combined = secWebSocketKey.Trim() + WEBSOCKET_GUID;
		using SHA1 sha1 = SHA1.Create();
		byte[] hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(combined));
		return Convert.ToBase64String(hashBytes);
	}

	/// <summary>
	/// Creates the response headers for a successful WebSocket upgrade
	/// </summary>
	public static NameValueCollection CreateUpgradeResponseHeaders(string secWebSocketKey)
		=> CreateUpgradeResponseHeaders(secWebSocketKey, null);

	/// <summary>
	/// Creates the response headers for a successful WebSocket upgrade, optionally including
	/// a negotiated subprotocol.
	/// </summary>
	public static NameValueCollection CreateUpgradeResponseHeaders(string secWebSocketKey, string? selectedSubProtocol)
	{
		NameValueCollection headers = new()
		{
			{ "Upgrade", "websocket" },
			{ "Connection", "Upgrade" },
			{ "Sec-WebSocket-Accept", ComputeAcceptKey(secWebSocketKey) }
		};

		if (!string.IsNullOrEmpty(selectedSubProtocol))
			headers.Add("Sec-WebSocket-Protocol", selectedSubProtocol);

		return headers;
	}

	/// <summary>
	/// Validates and extracts WebSocket key from request
	/// </summary>
	public static string GetSecWebSocketKey(HttpRequest request)
	{
		return request?.Headers["Sec-WebSocket-Key"] ?? "";
	}

	/// <summary>
	/// Returns the list of subprotocols requested by the client (in order), or empty if none.
	/// </summary>
	public static IReadOnlyList<string> GetRequestedSubProtocols(HttpRequest request)
	{
		string? raw = request?.Headers["Sec-WebSocket-Protocol"];
		if (string.IsNullOrEmpty(raw))
			return Array.Empty<string>();

		string[] parts = raw!.Split(',');
		List<string> result = new(parts.Length);
		foreach (string part in parts)
		{
			string trimmed = part.Trim();
			if (trimmed.Length > 0)
				result.Add(trimmed);
		}
		return result;
	}

	/// <summary>
	/// Selects the first client-requested subprotocol that is also in <paramref name="supportedSubProtocols"/>,
	/// preserving the client's preference order. Returns null if there is no overlap.
	/// </summary>
	public static string? SelectSubProtocol(HttpRequest request, IReadOnlyList<string>? supportedSubProtocols)
	{
		if (supportedSubProtocols is null || supportedSubProtocols.Count == 0)
			return null;

		IReadOnlyList<string> requested = GetRequestedSubProtocols(request);
		foreach (string r in requested)
		{
			foreach (string s in supportedSubProtocols)
			{
				if (string.Equals(r, s, StringComparison.Ordinal))
					return s;
			}
		}
		return null;
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
