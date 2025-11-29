using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using TR.SimpleHttpServer.WebSocket;

namespace TR.SimpleHttpServer;

public delegate Task<HttpResponse> HttpConnectionHandler(HttpRequest request);

public class HttpServer(
	IPAddress localAddress,
	ushort port,
	HttpConnectionHandler handler,
	WebSocketHandlerSelector? webSocketHandlerSelector = null
) : IDisposable
{
	public bool IsRunning => Listener.Server.IsBound;
	public ushort Port { get; } = port;
	private TcpListener Listener { get; } = new TcpListener(localAddress, port);
	private HttpConnectionHandler Handler { get; } = handler;
	private WebSocketHandlerSelector? WebSocketHandlerSelector { get; } = webSocketHandlerSelector;
	private CancellationTokenSource? CancellationTokenSource = null;

	public HttpServer(ushort port, HttpConnectionHandler handler) : this(IPAddress.Any, port, handler, null) { }

	public HttpServer(ushort port, HttpConnectionHandler handler, WebSocketHandlerSelector webSocketHandlerSelector) : this(IPAddress.Any, port, handler, webSocketHandlerSelector) { }

	public void Start()
	{
		if (disposedValue)
			throw new ObjectDisposedException(nameof(HttpServer));

		if (IsRunning)
			throw new InvalidOperationException("Server is already running.");

		CancellationTokenSource = new();
		Listener.Start();
		Task.Run(ListenTaskAsync).ContinueWith((task) =>
		{
			if (task.IsFaulted)
			{
				Console.Error.WriteLine(task.Exception);
			}
		});
	}

	public void Stop()
	{
		if (disposedValue)
			throw new ObjectDisposedException(nameof(HttpServer));

		if (!IsRunning)
			throw new InvalidOperationException("Server is not running.");

		CancellationTokenSource?.Cancel();
		Listener.Stop();
	}

	private async Task ListenTaskAsync()
	{
		if (disposedValue)
			throw new ObjectDisposedException(nameof(HttpServer));

		if (!IsRunning)
			throw new InvalidOperationException("Server is not running.");

		if (CancellationTokenSource is null || CancellationTokenSource.IsCancellationRequested)
			throw new InvalidOperationException("Server is not running.");

		CancellationToken cancellationToken = CancellationTokenSource.Token;
		while (!cancellationToken.IsCancellationRequested)
		{
			TcpClient client;
			try
			{
				client = await Listener.AcceptTcpClientAsync().ConfigureAwait(false);
			}
			catch (InvalidOperationException)
			{
				// ref: https://stackoverflow.com/questions/19220957/tcplistener-how-to-stop-listening-while-awaiting-accepttcpclientasync
				cancellationToken.ThrowIfCancellationRequested();
				throw;
			}
			catch (SocketException ex)
			{
				if (ex.SocketErrorCode == SocketError.Interrupted)
					break;
				else
					throw;
			}

			_ = Task
				.Run(async () =>
				{
					using ProcessOneConnectionWorker worker = new(client, cancellationToken, Handler, WebSocketHandlerSelector);
					await worker.ProcessAsync().ConfigureAwait(false);
				}, cancellationToken)
				.ContinueWith((task) =>
				{
					if (task.IsFaulted)
					{
						Console.Error.WriteLine(task.Exception);
					}

					client.Dispose();
				})
			;
		}

		if (IsRunning)
			Listener.Stop();
	}

	#region IDisposable Support
	private bool disposedValue;

	protected virtual void Dispose(bool disposing)
	{
		if (!disposedValue)
		{
			if (disposing)
			{
				if (CancellationTokenSource is not null)
				{
					CancellationTokenSource.Cancel();
					CancellationTokenSource.Dispose();
					CancellationTokenSource = null;
				}

				if (IsRunning)
					Listener.Stop();
			}

			disposedValue = true;
		}
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}
	#endregion IDisposable Support
}
