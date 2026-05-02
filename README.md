# SimpleHttpServerDotNet

シンプルで軽量な HTTP サーバーライブラリです。.NET Standard 2.0+ で動作し、HTTP リクエストを処理するための簡単なフレームワークを提供します。WebSocket 機能も完全にサポートしています。

> 📖 [English version](./README.en.md)

## 特徴

- ✅ **シンプルな API**: HTTP サーバーの構築が簡単
- ✅ **非同期処理**: Task ベースの非同期 API で高効率な処理
- ✅ **WebSocket 対応**: RFC 6455 準拠の WebSocket 通信をネイティブサポート
- ✅ **プロトコル検証**: RSV ビット・オペコード・マスク・フレームサイズなど RFC 6455 に基づく厳密な検証
- ✅ **Ping/Pong**: Ping フレームへの自動 Pong 返信とイベント通知
- ✅ **サブプロトコルネゴシエーション**: `Sec-WebSocket-Protocol` ヘッダーによるサブプロトコルの選択
- ✅ **.NET Standard 2.0 対応**: netstandard2.1, net8.0, net10.0 などとも互換
- ✅ **MIT ライセンス**: 自由に使用・改変可能

## プロジェクト構成

### TR.SimpleHttpServer

メインライブラリ。HTTP サーバー機能と WebSocket サポートを提供します。

**主なクラス:**

- `HttpServer`: HTTP サーバーの主要クラス
- `HttpRequest`: HTTP リクエストを表すクラス
- `HttpResponse`: HTTP レスポンスを表すクラス
- `WebSocketConnection`: WebSocket 接続を管理するクラス
- `WebSocketOptions`: WebSocket の動作設定クラス
- `WebSocketHandler`: WebSocket 処理のデリゲート
- `WebSocketProtocolException`: WebSocket プロトコル違反時にスローされる例外

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
		return HandleWebSocketConnection;
	return null;
}

// WebSocket接続処理を定義
async Task HandleWebSocketConnection(HttpRequest request, WebSocketConnection connection)
{
	while (connection.IsOpen)
	{
		var message = await connection.ReceiveMessageAsync(CancellationToken.None);

		if (message.Type == WebSocketMessageType.Close)
			break; // AutoRespondToClose=true (既定) により Close フレームは自動送信済み

		if (message.Type == WebSocketMessageType.Text)
		{
			string text = message.GetText();
			await connection.SendTextAsync($"Echo: {text}", CancellationToken.None);
		}
	}
}

// サーバーを作成・起動
using var server = new HttpServer(8080, HandleRequest, HandleWebSocketPath);
server.Start();
```

### WebSocket オプションを指定する場合

```csharp
using TR.SimpleHttpServer.WebSocket;

WebSocketOptions options = new()
{
	SupportedSubProtocols = new[] { "chat", "superchat" },
	MaxFrameSize = 4 * 1024 * 1024,    // 4 MB
	MaxMessageSize = 16 * 1024 * 1024, // 16 MB
	ValidateUtf8 = true,
};

using var server = new HttpServer(8080, HandleRequest, HandleWebSocketPath, options);
server.Start();
```

## API リファレンス

### HttpServer

```csharp
// HTTPサーバーを初期化
public HttpServer(ushort port, HttpConnectionHandler handler);
public HttpServer(ushort port, HttpConnectionHandler handler, WebSocketHandlerSelector webSocketHandlerSelector);
public HttpServer(ushort port, HttpConnectionHandler handler, WebSocketHandlerSelector webSocketHandlerSelector, WebSocketOptions webSocketOptions);
public HttpServer(IPAddress localAddress, ushort port, HttpConnectionHandler handler, WebSocketHandlerSelector? webSocketHandlerSelector = null, WebSocketOptions? webSocketOptions = null);

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

// ステータス文字列と文字列レスポンスボディで作成 (例: "404 Not Found")
public HttpResponse(string status, string contentType, NameValueCollection additionalHeaders, string body);

// ステータス文字列とバイナリレスポンスボディで作成
public HttpResponse(string status, string contentType, NameValueCollection additionalHeaders, byte[] body);

public string Status { get; }
public string ContentType { get; }
public byte[] Body { get; }
public NameValueCollection AdditionalHeaders { get; }
```

### WebSocketOptions

```csharp
public class WebSocketOptions
{
	// 単一フレームの最大ペイロードサイズ (既定: 16 MB)
	// 超過した場合は 1009 (Message Too Big) で接続を閉じる
	public int MaxFrameSize { get; set; }

	// フラグメント化されたメッセージの最大合計ペイロードサイズ (既定: 64 MB)
	public int MaxMessageSize { get; set; }

	// クライアントフレームのマスクを必須とするか (既定: true)
	// RFC 6455 §5.1 準拠。false の場合はテスト用途向け
	public bool RequireMasking { get; set; }

	// Close フレーム受信時に Close フレームを自動送信するか (既定: true)
	public bool AutoRespondToClose { get; set; }

	// テキストメッセージの UTF-8 妥当性を検証するか (既定: true)
	// 無効な場合は 1007 (Invalid Payload Data) で接続を閉じる
	public bool ValidateUtf8 { get; set; }

	// サーバーがサポートするサブプロトコルのリスト (優先順)
	// null または空の場合はサブプロトコルネゴシエーションを行わない
	public IReadOnlyList<string>? SupportedSubProtocols { get; set; }
}
```

### WebSocketConnection

```csharp
// コンストラクター
public WebSocketConnection(Stream stream);
public WebSocketConnection(Stream stream, WebSocketOptions options, string? selectedSubProtocol = null);

// 接続が開いているかどうかを確認
public bool IsOpen { get; }

// ハンドシェイクで選択されたサブプロトコル (ない場合は null)
public string? SelectedSubProtocol { get; }

// Ping フレーム受信時に発火するイベント
public event EventHandler<PingPongEventArgs>? PingReceived;

// Pong フレーム受信時に発火するイベント
public event EventHandler<PingPongEventArgs>? PongReceived;

// WebSocketメッセージを受信
// プロトコル違反時は WebSocketProtocolException をスロー (接続は既にクローズ済み)
public Task<WebSocketMessage> ReceiveMessageAsync(CancellationToken cancellationToken);

// テキストメッセージを送信
public Task SendTextAsync(string text, CancellationToken cancellationToken);

// バイナリメッセージを送信
public Task SendBinaryAsync(byte[] data, CancellationToken cancellationToken);

// Ping フレームを送信 (ペイロードは最大 125 バイト)
public Task SendPingAsync(byte[] data, CancellationToken cancellationToken);

// Close フレームを送信して接続をクローズ
public Task CloseAsync(WebSocketCloseStatus status, string reason, CancellationToken cancellationToken);
```

### WebSocketMessage

```csharp
public class WebSocketMessage
{
	// メッセージの種類 (Text / Binary / Close)
	public WebSocketMessageType Type { get; }

	// メッセージの生データ
	public byte[] Data { get; }

	// Close メッセージのステータスコード (Close 以外は null)
	public WebSocketCloseStatus? CloseStatus { get; }

	// Close メッセージの理由文字列 (Close 以外は空文字)
	public string CloseReason { get; }

	// データを UTF-8 文字列として取得
	public string GetText();
}
```

### WebSocketProtocolException

RFC 6455 プロトコル違反を検出した場合にスローされる例外です。
スローされる前に、対応するステータスコードで Close フレームが送信されます。

```csharp
public class WebSocketProtocolException : Exception
{
	// 送信された Close ステータスコード
	public WebSocketCloseStatus CloseStatus { get; }
}
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

```bash
dotnet test TR.SimpleHttpServer.Tests
```

## 対応フレームワーク

ライブラリは **netstandard2.0** をターゲットとしており、以下のランタイムで動作します:

- .NET Standard 2.0 / 2.1
- .NET 5 以降 (net8.0, net10.0 など)
- .NET Framework 4.6.1 以降

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
