using System.Collections.Specialized;
using System.Net;
using System.Text;

namespace TR.SimpleHttpServer;

public class HttpResponse(string status, HttpStatusCode statusCode, string ContentType, NameValueCollection additionalHeaders, byte[] body)
{
	public string Version { get; set; } = "HTTP/1.0";
	public string Status { get; } = status;
	public HttpStatusCode StatusCode { get; } = statusCode;
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
		(HttpStatusCode)int.Parse(status.Substring(0, 3)),
		ContentType,
		additionalHeaders,
		Encoding.UTF8.GetBytes(body)
	) { }

	public HttpResponse(
		HttpStatusCode status,
		string ContentType,
		NameValueCollection additionalHeaders,
		string body
	) : this(
		$"{(int)status} {GetHttpStatusCodeDescription(status)}",
		status,
		ContentType,
		additionalHeaders,
		Encoding.UTF8.GetBytes(body)
	) { }

	const int SPC_CHAR_CAPACITY = 16;
	public static string GetHttpStatusCodeDescription(HttpStatusCode code)
	{
		string str = code.ToString();
		StringBuilder sb = new(str.Length + SPC_CHAR_CAPACITY);
		for (int i = 0; i < str.Length; i++)
		{
			char c = str[i];
			if (i != 0 && char.IsUpper(c))
				sb.Append(' ');
			sb.Append(c);
		}
		return sb.ToString();
	}
}
