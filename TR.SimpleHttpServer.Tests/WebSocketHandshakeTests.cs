using System.Collections.Specialized;
using Xunit;
using TR.SimpleHttpServer.WebSocket;

namespace TR.SimpleHttpServer.Tests;

public class WebSocketHandshakeTests
{
	[Fact]
	public void IsWebSocketUpgradeRequest_ValidRequest_ReturnsTrue()
	{
		// Arrange
		NameValueCollection headers = new()
		{
			{ "Connection", "Upgrade" },
			{ "Upgrade", "websocket" },
			{ "Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ==" },
			{ "Sec-WebSocket-Version", "13" }
		};
		HttpRequest request = new("GET", "/ws", headers, new NameValueCollection(), System.Array.Empty<byte>());

		// Act
		bool result = WebSocketHandshake.IsWebSocketUpgradeRequest(request);

		// Assert
		Assert.True(result);
	}

	[Fact]
	public void IsWebSocketUpgradeRequest_NotGetMethod_ReturnsFalse()
	{
		// Arrange
		NameValueCollection headers = new()
		{
			{ "Connection", "Upgrade" },
			{ "Upgrade", "websocket" },
			{ "Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ==" },
			{ "Sec-WebSocket-Version", "13" }
		};
		HttpRequest request = new("POST", "/ws", headers, new NameValueCollection(), System.Array.Empty<byte>());

		// Act
		bool result = WebSocketHandshake.IsWebSocketUpgradeRequest(request);

		// Assert
		Assert.False(result);
	}

	[Fact]
	public void IsWebSocketUpgradeRequest_MissingConnectionHeader_ReturnsFalse()
	{
		// Arrange
		NameValueCollection headers = new()
		{
			{ "Upgrade", "websocket" },
			{ "Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ==" },
			{ "Sec-WebSocket-Version", "13" }
		};
		HttpRequest request = new("GET", "/ws", headers, new NameValueCollection(), System.Array.Empty<byte>());

		// Act
		bool result = WebSocketHandshake.IsWebSocketUpgradeRequest(request);

		// Assert
		Assert.False(result);
	}

	[Fact]
	public void IsWebSocketUpgradeRequest_WrongUpgradeHeader_ReturnsFalse()
	{
		// Arrange
		NameValueCollection headers = new()
		{
			{ "Connection", "Upgrade" },
			{ "Upgrade", "http/2" },
			{ "Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ==" },
			{ "Sec-WebSocket-Version", "13" }
		};
		HttpRequest request = new("GET", "/ws", headers, new NameValueCollection(), System.Array.Empty<byte>());

		// Act
		bool result = WebSocketHandshake.IsWebSocketUpgradeRequest(request);

		// Assert
		Assert.False(result);
	}

	[Fact]
	public void IsWebSocketUpgradeRequest_MissingSecWebSocketKey_ReturnsFalse()
	{
		// Arrange
		NameValueCollection headers = new()
		{
			{ "Connection", "Upgrade" },
			{ "Upgrade", "websocket" },
			{ "Sec-WebSocket-Version", "13" }
		};
		HttpRequest request = new("GET", "/ws", headers, new NameValueCollection(), System.Array.Empty<byte>());

		// Act
		bool result = WebSocketHandshake.IsWebSocketUpgradeRequest(request);

		// Assert
		Assert.False(result);
	}

	[Fact]
	public void IsWebSocketUpgradeRequest_WrongVersion_ReturnsFalse()
	{
		// Arrange
		NameValueCollection headers = new()
		{
			{ "Connection", "Upgrade" },
			{ "Upgrade", "websocket" },
			{ "Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ==" },
			{ "Sec-WebSocket-Version", "8" }
		};
		HttpRequest request = new("GET", "/ws", headers, new NameValueCollection(), System.Array.Empty<byte>());

		// Act
		bool result = WebSocketHandshake.IsWebSocketUpgradeRequest(request);

		// Assert
		Assert.False(result);
	}

	[Fact]
	public void IsWebSocketUpgradeRequest_ConnectionHeaderWithMultipleValues_ReturnsTrue()
	{
		// Arrange - Connection header can have multiple values (keep-alive, Upgrade)
		NameValueCollection headers = new()
		{
			{ "Connection", "keep-alive, Upgrade" },
			{ "Upgrade", "websocket" },
			{ "Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ==" },
			{ "Sec-WebSocket-Version", "13" }
		};
		HttpRequest request = new("GET", "/ws", headers, new NameValueCollection(), System.Array.Empty<byte>());

		// Act
		bool result = WebSocketHandshake.IsWebSocketUpgradeRequest(request);

		// Assert
		Assert.True(result);
	}

	[Fact]
	public void IsWebSocketUpgradeRequest_NullRequest_ReturnsFalse()
	{
		// Act
		bool result = WebSocketHandshake.IsWebSocketUpgradeRequest(null!);

		// Assert
		Assert.False(result);
	}

	[Fact]
	public void ComputeAcceptKey_ValidKey_ReturnsCorrectValue()
	{
		// Arrange - Using the example from RFC 6455
		string clientKey = "dGhlIHNhbXBsZSBub25jZQ==";

		// Act
		string acceptKey = WebSocketHandshake.ComputeAcceptKey(clientKey);

		// Assert - Expected value from RFC 6455 example
		Assert.Equal("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", acceptKey);
	}

	[Fact]
	public void ComputeAcceptKey_NullKey_ThrowsArgumentNullException()
	{
		// Act & Assert
		Assert.Throws<System.ArgumentNullException>(() => WebSocketHandshake.ComputeAcceptKey(null!));
	}

	[Fact]
	public void ComputeAcceptKey_EmptyKey_ThrowsArgumentNullException()
	{
		// Act & Assert
		Assert.Throws<System.ArgumentNullException>(() => WebSocketHandshake.ComputeAcceptKey(""));
	}

	[Fact]
	public void CreateUpgradeResponseHeaders_ValidKey_ContainsRequiredHeaders()
	{
		// Arrange
		string clientKey = "dGhlIHNhbXBsZSBub25jZQ==";

		// Act
		NameValueCollection headers = WebSocketHandshake.CreateUpgradeResponseHeaders(clientKey);

		// Assert
		Assert.Equal("websocket", headers["Upgrade"]);
		Assert.Equal("Upgrade", headers["Connection"]);
		Assert.Equal("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", headers["Sec-WebSocket-Accept"]);
	}

	[Fact]
	public void GetSecWebSocketKey_ValidRequest_ReturnsKey()
	{
		// Arrange
		NameValueCollection headers = new()
		{
			{ "Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ==" }
		};
		HttpRequest request = new("GET", "/ws", headers, new NameValueCollection(), System.Array.Empty<byte>());

		// Act
		string key = WebSocketHandshake.GetSecWebSocketKey(request);

		// Assert
		Assert.Equal("dGhlIHNhbXBsZSBub25jZQ==", key);
	}

	[Fact]
	public void GetSecWebSocketKey_MissingKey_ReturnsEmptyString()
	{
		// Arrange
		NameValueCollection headers = new();
		HttpRequest request = new("GET", "/ws", headers, new NameValueCollection(), System.Array.Empty<byte>());

		// Act
		string key = WebSocketHandshake.GetSecWebSocketKey(request);

		// Assert
		Assert.Equal("", key);
	}

	[Fact]
	public void GetSecWebSocketKey_NullRequest_ReturnsEmptyString()
	{
		// Act
		string key = WebSocketHandshake.GetSecWebSocketKey(null!);

		// Assert
		Assert.Equal("", key);
	}

	#region Error Cases

	[Fact]
	public void IsWebSocketUpgradeRequest_MissingUpgradeHeader_ReturnsFalse()
	{
		// Arrange
		NameValueCollection headers = new()
		{
			{ "Connection", "Upgrade" },
			{ "Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ==" },
			{ "Sec-WebSocket-Version", "13" }
		};
		HttpRequest request = new("GET", "/ws", headers, new NameValueCollection(), System.Array.Empty<byte>());

		// Act
		bool result = WebSocketHandshake.IsWebSocketUpgradeRequest(request);

		// Assert
		Assert.False(result);
	}

	[Fact]
	public void IsWebSocketUpgradeRequest_MissingVersion_ReturnsFalse()
	{
		// Arrange
		NameValueCollection headers = new()
		{
			{ "Connection", "Upgrade" },
			{ "Upgrade", "websocket" },
			{ "Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ==" }
		};
		HttpRequest request = new("GET", "/ws", headers, new NameValueCollection(), System.Array.Empty<byte>());

		// Act
		bool result = WebSocketHandshake.IsWebSocketUpgradeRequest(request);

		// Assert
		Assert.False(result);
	}

	[Fact]
	public void IsWebSocketUpgradeRequest_EmptyHeaders_ReturnsFalse()
	{
		// Arrange
		NameValueCollection headers = new();
		HttpRequest request = new("GET", "/ws", headers, new NameValueCollection(), System.Array.Empty<byte>());

		// Act
		bool result = WebSocketHandshake.IsWebSocketUpgradeRequest(request);

		// Assert
		Assert.False(result);
	}

	[Fact]
	public void IsWebSocketUpgradeRequest_ConnectionNotContainingUpgrade_ReturnsFalse()
	{
		// Arrange
		NameValueCollection headers = new()
		{
			{ "Connection", "keep-alive" },
			{ "Upgrade", "websocket" },
			{ "Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ==" },
			{ "Sec-WebSocket-Version", "13" }
		};
		HttpRequest request = new("GET", "/ws", headers, new NameValueCollection(), System.Array.Empty<byte>());

		// Act
		bool result = WebSocketHandshake.IsWebSocketUpgradeRequest(request);

		// Assert
		Assert.False(result);
	}

	[Fact]
	public void IsWebSocketUpgradeRequest_UpgradeNotWebsocket_ReturnsFalse()
	{
		// Arrange
		NameValueCollection headers = new()
		{
			{ "Connection", "Upgrade" },
			{ "Upgrade", "h2c" }, // HTTP/2 cleartext
			{ "Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ==" },
			{ "Sec-WebSocket-Version", "13" }
		};
		HttpRequest request = new("GET", "/ws", headers, new NameValueCollection(), System.Array.Empty<byte>());

		// Act
		bool result = WebSocketHandshake.IsWebSocketUpgradeRequest(request);

		// Assert
		Assert.False(result);
	}

	[Theory]
	[InlineData("8")]
	[InlineData("10")]
	[InlineData("12")]
	[InlineData("14")]
	[InlineData("")]
	[InlineData("abc")]
	public void IsWebSocketUpgradeRequest_InvalidVersion_ReturnsFalse(string version)
	{
		// Arrange
		NameValueCollection headers = new()
		{
			{ "Connection", "Upgrade" },
			{ "Upgrade", "websocket" },
			{ "Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ==" },
			{ "Sec-WebSocket-Version", version }
		};
		HttpRequest request = new("GET", "/ws", headers, new NameValueCollection(), System.Array.Empty<byte>());

		// Act
		bool result = WebSocketHandshake.IsWebSocketUpgradeRequest(request);

		// Assert
		Assert.False(result);
	}

	[Theory]
	[InlineData("HEAD")]
	[InlineData("PUT")]
	[InlineData("DELETE")]
	[InlineData("PATCH")]
	[InlineData("OPTIONS")]
	public void IsWebSocketUpgradeRequest_NonGetMethod_ReturnsFalse(string method)
	{
		// Arrange
		NameValueCollection headers = new()
		{
			{ "Connection", "Upgrade" },
			{ "Upgrade", "websocket" },
			{ "Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ==" },
			{ "Sec-WebSocket-Version", "13" }
		};
		HttpRequest request = new(method, "/ws", headers, new NameValueCollection(), System.Array.Empty<byte>());

		// Act
		bool result = WebSocketHandshake.IsWebSocketUpgradeRequest(request);

		// Assert
		Assert.False(result);
	}

	[Fact]
	public void IsWebSocketUpgradeRequest_EmptySecWebSocketKey_ReturnsFalse()
	{
		// Arrange
		NameValueCollection headers = new()
		{
			{ "Connection", "Upgrade" },
			{ "Upgrade", "websocket" },
			{ "Sec-WebSocket-Key", "" },
			{ "Sec-WebSocket-Version", "13" }
		};
		HttpRequest request = new("GET", "/ws", headers, new NameValueCollection(), System.Array.Empty<byte>());

		// Act
		bool result = WebSocketHandshake.IsWebSocketUpgradeRequest(request);

		// Assert
		Assert.False(result);
	}

	[Fact]
	public void ComputeAcceptKey_WhitespaceKey_ReturnsValidKey()
	{
		// Arrange
		string whitespaceKey = "   ";

		// Act - Whitespace-only keys are treated as valid (trimmed in implementation)
		string result = WebSocketHandshake.ComputeAcceptKey(whitespaceKey);

		// Assert - Should not throw and should return a valid base64 string
		Assert.NotEmpty(result);
	}

	[Fact]
	public void CreateUpgradeResponseHeaders_EmptyKey_ThrowsArgumentNullException()
	{
		// Act & Assert
		Assert.Throws<System.ArgumentNullException>(() => WebSocketHandshake.CreateUpgradeResponseHeaders(""));
	}

	[Fact]
	public void CreateUpgradeResponseHeaders_NullKey_ThrowsArgumentNullException()
	{
		// Act & Assert
		Assert.Throws<System.ArgumentNullException>(() => WebSocketHandshake.CreateUpgradeResponseHeaders(null!));
	}

	[Fact]
	public void IsWebSocketUpgradeRequest_CaseInsensitiveUpgradeHeader_ReturnsTrue()
	{
		// Arrange
		NameValueCollection headers = new()
		{
			{ "Connection", "UPGRADE" },
			{ "Upgrade", "WEBSOCKET" },
			{ "Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ==" },
			{ "Sec-WebSocket-Version", "13" }
		};
		HttpRequest request = new("GET", "/ws", headers, new NameValueCollection(), System.Array.Empty<byte>());

		// Act
		bool result = WebSocketHandshake.IsWebSocketUpgradeRequest(request);

		// Assert
		Assert.True(result);
	}

	[Fact]
	public void GetSecWebSocketKey_KeyWithWhitespace_ReturnsKey()
	{
		// Arrange
		NameValueCollection headers = new()
		{
			{ "Sec-WebSocket-Key", "  dGhlIHNhbXBsZSBub25jZQ==  " }
		};
		HttpRequest request = new("GET", "/ws", headers, new NameValueCollection(), System.Array.Empty<byte>());

		// Act
		string key = WebSocketHandshake.GetSecWebSocketKey(request);

		// Assert
		// The key should be returned as-is, trimming is up to the caller if needed
		Assert.Contains("dGhlIHNhbXBsZSBub25jZQ==", key);
	}

	#endregion
}
