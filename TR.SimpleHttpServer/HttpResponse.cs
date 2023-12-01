using System.Collections.Specialized;
using System.Text;

namespace TR.SimpleHttpServer;

public class HttpResponse(string status, string ContentType, NameValueCollection additionalHeaders, byte[] body)
{
	public string Status { get; } = status;
	public NameValueCollection AdditionalHeaders { get; } = additionalHeaders;
	public string ContentType { get; } = ContentType;
	public byte[] Body { get; } = body;

	public HttpResponse(
		string status,
		string ContentType,
		NameValueCollection additionalHeaders,
		string body
	) : this(
		status,
		ContentType,
		additionalHeaders,
		Encoding.UTF8.GetBytes(body)
	) { }
}
