namespace TR.SimpleHttpServer.WebSocket;

/// <summary>
/// WebSocket close status codes (RFC 6455 Section 7.4.1)
/// </summary>
public enum WebSocketCloseStatus : ushort
{
	/// <summary>
	/// Normal closure (1000)
	/// </summary>
	NormalClosure = 1000,

	/// <summary>
	/// Going away (1001)
	/// </summary>
	GoingAway = 1001,

	/// <summary>
	/// Protocol error (1002)
	/// </summary>
	ProtocolError = 1002,

	/// <summary>
	/// Unsupported data type (1003)
	/// </summary>
	UnsupportedData = 1003,

	/// <summary>
	/// No status received (1005)
	/// </summary>
	NoStatusReceived = 1005,

	/// <summary>
	/// Abnormal closure (1006)
	/// </summary>
	AbnormalClosure = 1006,

	/// <summary>
	/// Invalid frame payload data (1007)
	/// </summary>
	InvalidPayloadData = 1007,

	/// <summary>
	/// Policy violation (1008)
	/// </summary>
	PolicyViolation = 1008,

	/// <summary>
	/// Message too big (1009)
	/// </summary>
	MessageTooBig = 1009,

	/// <summary>
	/// Mandatory extension (1010)
	/// </summary>
	MandatoryExtension = 1010,

	/// <summary>
	/// Internal server error (1011)
	/// </summary>
	InternalServerError = 1011
}
