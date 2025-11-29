# SimpleHttpServerDotNet

シンプルで軽量な HTTP サーバーライブラリです。.NET Standard 2.0+ で動作し、HTTP リクエストを処理するための簡単なフレームワークを提供します。WebSocket 機能も完全にサポートしています。

> 📖 [English version](./README.en.md)

## 特徴

- ✅ **シンプルな API**: HTTP サーバーの構築が簡単
- ✅ **非同期処理**: Task ベースの非同期 API で高効率な処理
- ✅ **WebSocket 対応**: WebSocket 通信をネイティブサポート
- ✅ **マルチフレームワーク対応**: netstandard2.0, netstandard2.1, net8.0, net10.0 をサポート
- ✅ **MIT ライセンス**: 自由に使用・改変可能

## プロジェクト構成

### TR.SimpleHttpServer

メインライブラリ。HTTP サーバー機能と WebSocket サポートを提供します。

**主なクラス:**

- `HttpServer`: HTTP サーバーの主要クラス
- `HttpRequest`: HTTP リクエストを表すクラス
- `HttpResponse`: HTTP レスポンスを表すクラス
- `WebSocketConnection`: WebSocket 接続を管理するクラス
- `WebSocketHandler`: WebSocket 処理のデリゲート

### TR.SimpleHttpServer.Host

ライブラリの使用例を含むホストアプリケーション。

- HTTP エンドポイント（静的ファイル配信）
- WebSocket エコーエンドポイント
- WebSocket チャットアプリケーション

### TR.SimpleHttpServer.Tests

ユニットテストと WebSocket 統合テスト。

## インストール

### NuGet を使用する場合

```bash
dotnet add package TR.SimpleHttpServer
```

### ソースコードからビルドする場合

```bash
git clone https://github.com/TetsuOtter/SimpleHttpServerDotNet.git
cd SimpleHttpServerDotNet
dotnet build TR.SimpleHttpServer.sln
```

## 使い方

### 基本的な HTTP サーバー

```csharp
using TR.SimpleHttpServer;
using System.Net;

// HTTPリクエスト処理のハンドラを定義
async Task<HttpResponse> HandleRequest(HttpRequest request)
{
	return new HttpResponse(
		HttpStatusCode.OK,
		"text/plain",
		new System.Collections.Specialized.NameValueCollection(),
		$"Hello, {request.Path}!"
	);
}

// サーバーを作成・起動
using var server = new HttpServer(8080, HandleRequest);
server.Start();

Console.WriteLine("Server is running on http://localhost:8080/");
Console.ReadKey();
```

### WebSocket 対応サーバー

```csharp
using TR.SimpleHttpServer;
using TR.SimpleHttpServer.WebSocket;

// WebSocketハンドラセレクタを定義
async Task<WebSocketHandler?> HandleWebSocketPath(string path)
{
	if (path == "/ws")
	{
		return HandleWebSocketConnection;
	}
	return null;
}

// WebSocket接続処理を定義
async Task HandleWebSocketConnection(HttpRequest request, WebSocketConnection connection)
{
	while (connection.IsOpen)
	{
		var message = await connection.ReceiveMessageAsync(CancellationToken.None);

		if (message.Type == WebSocketMessageType.Close)
		{
			await connection.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
			break;
		}

		if (message.Type == WebSocketMessageType.Text)
		{
			// テキストメッセージをエコーバック
			string text = message.GetText();
			await connection.SendTextAsync($"Echo: {text}", CancellationToken.None);
		}
	}
}

// サーバーを作成・起動
using var server = new HttpServer(8080, HandleRequest, HandleWebSocketPath);
server.Start();
```

## API リファレンス

### HttpServer

```csharp
// HTTPサーバーを初期化
public HttpServer(ushort port, HttpConnectionHandler handler);
public HttpServer(ushort port, HttpConnectionHandler handler, WebSocketHandlerSelector webSocketHandlerSelector);
public HttpServer(IPAddress localAddress, ushort port, HttpConnectionHandler handler, WebSocketHandlerSelector? webSocketHandlerSelector = null);

// サーバーを開始
public void Start();

// サーバーを停止
public void Stop();

// サーバーが実行中かどうかを確認
public bool IsRunning { get; }

// バインドポート番号
public ushort Port { get; }
```

### HttpRequest

```csharp
public class HttpRequest
{
	// HTTPメソッド (GET, POST, etc.)
	public string Method { get; }

	// リクエストパス
	public string Path { get; }

	// HTTPヘッダー
	public NameValueCollection Headers { get; }

	// クエリ文字列パラメータ
	public NameValueCollection QueryString { get; }

	// リクエストボディ
	public byte[] Body { get; }
}
```

### HttpResponse

```csharp
// ステータスコードと文字列レスポンスボディで作成
public HttpResponse(HttpStatusCode status, string contentType, NameValueCollection additionalHeaders, string body);

// ステータスコードとバイナリレスポンスボディで作成
public HttpResponse(HttpStatusCode status, string contentType, NameValueCollection additionalHeaders, byte[] body);

public string Status { get; }
public string ContentType { get; }
public byte[] Body { get; }
public NameValueCollection AdditionalHeaders { get; }
```

### WebSocketConnection

```csharp
// WebSocketメッセージを受信
public Task<WebSocketMessage> ReceiveMessageAsync(CancellationToken cancellationToken);

// テキストメッセージを送信
public Task SendTextAsync(string text, CancellationToken cancellationToken);

// バイナリメッセージを送信
public Task SendBinaryAsync(byte[] data, CancellationToken cancellationToken);

// WebSocket接続をクローズ
public Task CloseAsync(WebSocketCloseStatus status, string statusDescription, CancellationToken cancellationToken);

// 接続が開いているかどうかを確認
public bool IsOpen { get; }
```

## ホストアプリケーション

付属の `TR.SimpleHttpServer.Host` アプリケーションは以下のエンドポイントを提供しています:

```bash
dotnet run --project TR.SimpleHttpServer.Host
```

- **HTTP**: `http://localhost:8080/` - インデックスページ
- **WebSocket echo**: `ws://localhost:8080/ws` - メッセージをエコーバック
- **WebSocket chat**: `ws://localhost:8080/chat-ws` - マルチユーザーチャット

## テスト

### ユニットテストの実行

```bash
dotnet test TR.SimpleHttpServer.Tests
```

### E2E テストの実行

WebSocket の統合テスト:

```bash
# サーバーを起動
dotnet run --project TR.SimpleHttpServer.Host &

# テストを実行
cd e2e-tests
pip install -r requirements.txt
pytest -v
```

## 対応フレームワーク

- .NET Standard 2.0

## ライセンス

MIT License - 詳細は [LICENSE](LICENSE) を参照

## 貢献

バグ報告や機能提案は Issue を、コード改善は Pull Request をお待ちしています。

## 作者

Tetsu Otter (Tech Otter)

## 参考リソース

- [GitHub リポジトリ](https://github.com/TetsuOtter/SimpleHttpServerDotNet)
- [WebSocket RFC 6455](https://tools.ietf.org/html/rfc6455)
- [HTTP/1.1 RFC 7230](https://tools.ietf.org/html/rfc7230)
