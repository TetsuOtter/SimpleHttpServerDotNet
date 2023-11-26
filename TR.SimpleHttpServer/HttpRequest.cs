using System.Collections.Specialized;

namespace TR.SimpleHttpServer;

public class HttpRequest(
	string method,
	string path,
	NameValueCollection headers,
	NameValueCollection queryString,
	byte[] body
)
{
	public string Method { get; } = method;
	public string Path { get; } = path;
	public NameValueCollection Headers { get; } = headers;
	public NameValueCollection QueryString { get; } = queryString;
	public byte[] Body { get; } = body;
}
