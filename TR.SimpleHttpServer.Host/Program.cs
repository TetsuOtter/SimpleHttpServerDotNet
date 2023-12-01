using System;
using System.Threading;
using System.Threading.Tasks;

namespace TR.SimpleHttpServer.Host;

class Program : IDisposable
{
	static void Main(string[] args)
	{
		try
		{
			using Program program = new();

			program.Start();
			Console.WriteLine($"Server is running on port {program.server.Port}.");

			if (Console.IsInputRedirected)
			{
				Thread.Sleep(Timeout.Infinite);
			}
			else
			{
				Console.WriteLine("Press any key to stop server.");
				Console.ReadKey();
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine(ex);
		}
	}

	readonly HttpServer server;

	public Program()
	{
		server = new HttpServer(8080, HandleRequest);
	}

	public void Start() => server.Start();

	static Task<HttpResponse> HandleRequest(HttpRequest request)
	{
		HttpResponse response = new("200 OK", "text/plain", [], $"Hello, World!\nThank you for requesting {request.Path} with method {request.Method}!");
		return Task.FromResult(response);
	}

  public void Dispose() => ((IDisposable)server).Dispose();
}
