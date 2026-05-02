using System;
using System.ComponentModel;

namespace TR.SimpleHttpServer.WebSocket;

/// <summary>
/// Represents a WebSocket frame (RFC 6455)
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public class WebSocketFrame
{
	/// <summary>
	/// Indicates if this is the final fragment
	/// </summary>
	public bool IsFinal { get; }

	/// <summary>
	/// RSV1 bit (must be 0 unless an extension defines it)
	/// </summary>
	public bool Rsv1 { get; }

	/// <summary>
	/// RSV2 bit (must be 0 unless an extension defines it)
	/// </summary>
	public bool Rsv2 { get; }

	/// <summary>
	/// RSV3 bit (must be 0 unless an extension defines it)
	/// </summary>
	public bool Rsv3 { get; }

	/// <summary>
	/// Frame opcode
	/// </summary>
	public WebSocketOpcode Opcode { get; }

	/// <summary>
	/// Indicates if the payload is masked
	/// </summary>
	public bool IsMasked { get; }

	/// <summary>
	/// Masking key (4 bytes, if masked)
	/// </summary>
	public byte[] MaskingKey { get; }

	/// <summary>
	/// Payload data (unmasked)
	/// </summary>
	public byte[] Payload { get; }

	/// <summary>
	/// Creates a new WebSocket frame, including reserved bits
	/// </summary>
	public WebSocketFrame(bool isFinal, bool rsv1, bool rsv2, bool rsv3, WebSocketOpcode opcode, bool isMasked, byte[] maskingKey, byte[] payload)
	{
		if (isMasked && maskingKey.Length != 4)
			throw new ArgumentException("Masking key must be 4 bytes when masked", nameof(maskingKey));

		IsFinal = isFinal;
		Rsv1 = rsv1;
		Rsv2 = rsv2;
		Rsv3 = rsv3;
		Opcode = opcode;
		IsMasked = isMasked;
		MaskingKey = maskingKey;
		Payload = payload;
	}

	/// <summary>
	/// Creates a new WebSocket frame with no reserved bits set
	/// </summary>
	public WebSocketFrame(bool isFinal, WebSocketOpcode opcode, bool isMasked, byte[] maskingKey, byte[] payload)
		: this(isFinal, false, false, false, opcode, isMasked, maskingKey, payload)
	{
	}

	/// <summary>
	/// Creates an unmasked WebSocket frame
	/// </summary>
	public WebSocketFrame(bool isFinal, WebSocketOpcode opcode, byte[] payload)
		: this(isFinal, false, false, false, opcode, false, Array.Empty<byte>(), payload)
	{
	}

	/// <summary>
	/// Encodes the frame to bytes for transmission
	/// </summary>
	public byte[] ToBytes()
	{
		int payloadLength = Payload.Length;
		int headerLength = 2;

		// Determine extended payload length size
		if (payloadLength > 125 && payloadLength <= 65535)
			headerLength += 2;
		else if (payloadLength > 65535)
			headerLength += 8;

		// Add masking key size if masked
		if (IsMasked)
			headerLength += 4;

		byte[] frame = new byte[headerLength + payloadLength];
		int offset = 0;

		// First byte: FIN + RSV1/2/3 + Opcode
		byte firstByte = (byte)((int)Opcode & 0x0F);
		if (IsFinal) firstByte |= 0x80;
		if (Rsv1) firstByte |= 0x40;
		if (Rsv2) firstByte |= 0x20;
		if (Rsv3) firstByte |= 0x10;
		frame[offset++] = firstByte;

		// Second byte: MASK + Payload length
		byte maskBit = (byte)(IsMasked ? 0x80 : 0x00);
		if (payloadLength <= 125)
		{
			frame[offset++] = (byte)(maskBit | payloadLength);
		}
		else if (payloadLength <= 65535)
		{
			frame[offset++] = (byte)(maskBit | 126);
			frame[offset++] = (byte)((payloadLength >> 8) & 0xFF);
			frame[offset++] = (byte)(payloadLength & 0xFF);
		}
		else
		{
			frame[offset++] = (byte)(maskBit | 127);
			for (int i = 7; i >= 0; i--)
			{
				frame[offset++] = (byte)((payloadLength >> (i * 8)) & 0xFF);
			}
		}

		// Masking key (if masked)
		if (IsMasked)
		{
			Array.Copy(MaskingKey, 0, frame, offset, 4);
			offset += 4;
		}

		// Payload (masked if needed)
		if (IsMasked)
		{
			for (int i = 0; i < payloadLength; i++)
			{
				frame[offset + i] = (byte)(Payload[i] ^ MaskingKey[i % 4]);
			}
		}
		else
		{
			Array.Copy(Payload, 0, frame, offset, payloadLength);
		}

		return frame;
	}

	/// <summary>
	/// Apply masking/unmasking to data
	/// </summary>
	public static byte[] ApplyMask(byte[] data, byte[] maskingKey)
	{
		if (maskingKey.Length != 4)
			throw new ArgumentException("Masking key must be 4 bytes", nameof(maskingKey));

		byte[] result = new byte[data.Length];
		for (int i = 0; i < data.Length; i++)
		{
			result[i] = (byte)(data[i] ^ maskingKey[i % 4]);
		}
		return result;
	}
}
