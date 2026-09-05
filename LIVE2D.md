# Live2DアセットAPI

`GetLive2DAsset` は既存の .NET 10 / Azure Functions Isolated + ASP.NET Core 構成に合わせたHTTP関数です。既存DIの `BlobServiceClient` と接続設定 `StorageConnection` を再利用します。`Program.cs`、パッケージ、本番設定の追加変更は不要です。

## 配置とアクセス

`StorageConnection` が指すStorage Accountに **private** コンテナ `live2d` を用意し、Cubism書き出し時の相対パスを保持してアップロードします。

```text
live2d/
  penguin/
    1254.model3.json
    1254.moc3
    1254.physics3.json
    1254cdi3.json
    textures/...
```

取得例: `GET /api/live2d/penguin/1254.model3.json`

ルートは `live2d/{model}/{*path}` です。`api` はFunctions標準のプレフィックスです。`model` はフォルダ名（`penguin`）、`path` はその下のファイルパス（`1254.model3.json` や `textures/texture_00.png`）です。ファイル名の変更は不要です。JSON内部の参照と実際のBlob名は、大文字小文字も含めて一致させてください。

## 応答

- 200: Blobをストリームで返します。JSONは `application/json`、PNGは `image/png`、JPEG/WebP/WAV/MP3/OGGも拡張子に対応したContent-Type、`.moc3` と未知の拡張子は `application/octet-stream` です。Blobに既定のContent-Typeが設定されていても拡張子を優先します。
- 成功時に `Cache-Control: public, max-age=3600` と `X-Content-Type-Options: nosniff` を設定します。同じURLの更新反映には最大1時間かかる場合があります。
- 404: Blobまたはコンテナが存在しません。
- 400: 空のパス要素、`.` / `..`、バックスラッシュ、制御文字、先頭末尾の空白、末尾のドット、`%` / `:` / `?` / `#`、1024文字超のBlob名を拒否します。残ったパーセントエンコードも拒否し、二重デコードしません。ホストやブラウザが先に正規化・拒否するURLは、この関数へそのまま届かない場合があります。
- Storageの認証エラーや障害は404に変換せず、通常のFunctionsエラーとして扱います。

既存の公開用APIと同じ `AuthorizationLevel.Anonymous` です。Blobコンテナはprivateのままですが、このAPIから `live2d` 内の資産は誰でも取得できます。ブラウザから別オリジンのFunctionsへアクセスする場合は、Function App側のCORSにフロントエンドのオリジンを設定します。ブラウザからBlobへ直接アクセスしないためBlob側のCORSは不要です。

表情ボタンやフロントエンドは今回の変更に含みません。

## 検証

```sh
dotnet build PortfolioUtilityFunctions/PortfolioUtilityFunctions.csproj --configuration Release
dotnet test PortfolioUtilityFunctions.Tests/PortfolioUtilityFunctions.Tests.csproj --configuration Release
```

テストはBlobクライアントを差し替え、ネストしたパスの対応、パス検証、Content-Type、404と障害の区別、キャッシュ、キャンセルを検証します。実環境では上記URLとJSONから参照される各ファイルを取得し、応答ヘッダーと本文を確認してください。

実施結果: Releaseビルド成功（警告・エラーなし）、自動テスト43件成功。生成されたFunctionsメタデータでもGET・Anonymous・catch-allルートを確認しました。実Azureへの接続とFunctionsホスト経由のHTTP動作は未検証です。

Windowsの長い作業パスではFunctions拡張機能の生成中にMSB3030が発生しました。同一ソースを短い作業パスへコピーしてビルド・テストは成功しています。同様のエラー時はチェックアウト先を短くしてください。
