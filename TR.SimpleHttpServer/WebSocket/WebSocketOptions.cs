using System.Collections.Generic;

namespace TR.SimpleHttpServer.WebSocket;

/// <summary>
/// Configuration options for WebSocket connections (RFC 6455)
/// </summary>
public class WebSocketOptions
{
	/// <summary>Default maximum frame payload size (16 MB)</summary>
	public const int DefaultMaxFrameSize = 16 * 1024 * 1024;

	/// <summary>Default maximum accumulated message size (64 MB)</summary>
	public const int DefaultMaxMessageSize = 64 * 1024 * 1024;

	/// <summary>
	/// Maximum payload size for a single frame in bytes.
	/// A frame exceeding this size causes the connection to close with 1009 (Message Too Big).
	/// </summary>
	public int MaxFrameSize { get; set; } = DefaultMaxFrameSize;

	/// <summary>
	/// Maximum total payload size for a fragmented message in bytes.
	/// A message exceeding this size causes the connection to close with 1009 (Message Too Big).
	/// </summary>
	public int MaxMessageSize { get; set; } = DefaultMaxMessageSize;

	/// <summary>
	/// Whether client frames are required to be masked (RFC 6455 Section 5.1).
	/// When true (default), an unmasked client frame causes the connection to close with 1002 (Protocol Error).
	/// </summary>
	public bool RequireMasking { get; set; } = true;

	/// <summary>
	/// Whether to automatically reply to a received Close frame with a Close frame (RFC 6455 Section 5.5.1).
	/// </summary>
	public bool AutoRespondToClose { get; set; } = true;

	/// <summary>
	/// Whether to validate UTF-8 of received Text messages (RFC 6455 Section 8.1).
	/// Invalid UTF-8 closes the connection with 1007 (Invalid Payload Data).
	/// Note: validation runs once on the assembled message, not byte-by-byte across fragments,
	/// so the Autobahn 6.4.* fast-fail tests will not pass without an incremental validator.
	/// </summary>
	public bool ValidateUtf8 { get; set; } = true;

	/// <summary>
	/// Subprotocols this server supports, in priority order.
	/// During handshake the server selects the first client-requested protocol that appears in this list.
	/// If null or empty, no subprotocol negotiation is performed.
	/// </summary>
	public IReadOnlyList<string>? SupportedSubProtocols { get; set; }
}
