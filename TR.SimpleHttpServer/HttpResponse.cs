using System.Collections.Specialized;

namespace TR.SimpleHttpServer;

public class HttpResponse(string status, string ContentType, NameValueCollection additionalHeaders, byte[] body)
{
	public string Status { get; } = status;
	public NameValueCollection AdditionalHeaders { get; } = additionalHeaders;
	public string ContentType { get; } = ContentType;
	public byte[] Body { get; } = body;
}
