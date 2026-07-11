using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Serialization.HybridRow;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Portfolio.Shared.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using System.Net;
using System.Text.Json;

namespace PortfolioUtilityFunctions
{
    // 動作確認方法
    // 1. Azuriteをインストール・起動する
    // npm install -g azurite
    // 起動は、local.settings.jsonに"AzureWebJobsStorage","FUNCTIONS_WORKER_RUNTIME","StorageConnection"の3つの設定があればよい。このプログラムのデバッグ実行時に、Azuriteが起動するはず。
    // 2. Azure Storage ExplorerでAzuriteのBlob Storageにアクセスする。
    // インストールすれば自動でAzuriteのBlob Storageにアクセスできるようになるので、そこに、"works-full", "works-thumb"というコンテナを作る。
    // 3. この関数をデバッグ実行する。
    // 4. Azure Storage Explorerで、"works-full"コンテナに画像をアップロードする。
    // 5. アップロードした画像のサムネイルが、"works-thumb"コンテナに生成されることを確認する。

    // CosmosDBの接続文字列は、local.settings.jsonに"CosmosDB__ConnectionString"という名前で設定しておくこと。

    // CosmosDBにはWorksコンテナを作っておくこと。PartitionKeyは/idにすること。
    // 以下のコードでWorksコンテナが無い場合に自動的に作成できる。実務ではこっちの方が良い。
    //var database = _cosmosClient.GetDatabase("PortfolioDB");
    //var containerResponse = await database.CreateContainerIfNotExistsAsync(
    //    id: "Works",
    //    partitionKeyPath: "/id");
    //var container = containerResponse.Container;

    // azuriteではそのまま動作するが、AzureではBlobストレージアカウントのイベントサブスクリプションにFunctionsを登録する必要がある。
    // この設定のためにはAzureサブスクリプションのリソース プロバイダーにMicrosoft.EventGridを登録する必要がある。Azureポータルのサブスクリプションの「リソース プロバイダー」でMicrosoft.EventGridを検索して登録する。

    /// <summary>
    /// データを生成する関数
    /// </summary>
    public class GenerateData(CosmosClient cosmosClient, BlobServiceClient blobServiceClient, ILogger<GenerateData> logger)
    {
        private readonly string _databaseId = "PortfolioDB";
        private readonly Size BannerThumbSize = new(280, 280);  // 正方形ならバナー、長方形ならフライヤーとする。
        private readonly Size FlyerThumbSize = new(248, 350);
        private readonly string FullContainerName = "works-full";
        private readonly string ThumbContainerName = "works-thumb";

        // BlobTriggerから、Microsoft推奨のEventGridTriggerに変更する。BlobTriggerは、Azure Functionsのv5以降では非推奨になったため。
        // BlobTriggerの場合、Webhook URLを組み立てる必要があるが、EventGridTriggerの場合は、Azure PortalのEvent Gridのサブスクリプションを作成するだけで済む。
        // BlobTriggerはBlobの中身をStreamで渡してくれるが、レイテンシと信頼性の問題があるため本番運用には向かない。
        // EventGridTriggerはBlobの中身を渡してくれないが、BlobのURLを渡してくれるので、Blobの中身を取得する処理を自分で書く必要がある。


        /// <summary>
        /// サムネイルを生成するBlobTrigger関数
        /// 画像を"works-full"というBlobコンテナに入れると、"works-thumb"というBlobコンテナにwebpサムネイルとして保存する。
        /// </summary>
        /// <param name="inputBlob"></param>
        /// <param name="name">対象の拡張子付き画像ファイル名</param>
        /// <param name="context"></param>
        /// <returns></returns>
        [Function("GenerateThumbnail")]
        public async Task Run([EventGridTrigger] string data)
        {
            // Jsonで送られてくるEventGridのデータをパースする
            // Blobの場所やファイル名を取得する
            using var doc = JsonDocument.Parse(data);
            var eventType = doc.RootElement.GetProperty("eventType").GetString();
            var subject = doc.RootElement.GetProperty("subject").GetString(); // 例: /blobServices/default/containers/works-full/blobs/1.png

            // BlobCreated以外は無視, works-fullコンテナ以外は無視
            if (eventType != "Microsoft.Storage.BlobCreated" || subject is null || !subject.Contains($"/containers/{FullContainerName}/"))
            {
                return;
            }

            // subjectからファイル名を抜き出す
            var name = subject.Split('/').Last();
            logger.LogInformation($"サムネイル生成開始: {name}");
            var sourceContainer = blobServiceClient.GetBlobContainerClient(FullContainerName);

            // Blobを取得する
            var sourceBlob = sourceContainer.GetBlobClient(name);
            using var inputStream = await sourceBlob.OpenReadAsync();

            // 画像を読み込んで、サイズを既定のサムネイルサイズに縮小する
            using var image = await Image.LoadAsync(inputStream);
            var thumbSize = image.Size.Width == image.Size.Height ? BannerThumbSize : FlyerThumbSize; 
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = thumbSize
            }));

            // もう1つのBlob Storageにサムネイルを保存する
            var thumbFileName = Path.ChangeExtension(name, ".webp");
            var thumbBlob = blobServiceClient.GetBlobContainerClient(ThumbContainerName).GetBlobClient(thumbFileName);    // works-full（フルサイズ原本）, works-thumb(サムネイル)

            // 既に存在するかチェック
            var existsResponse = await thumbBlob.ExistsAsync();
            if (existsResponse.Value)
            {
                throw new InvalidOperationException(
                    $"サムネイル '{thumbFileName}' は既に存在します。ファイル名の衝突の可能性があるため処理を中断しました。元ファイル: {name}");
            }

            // サムネイルをwebp形式で保存する
            using var outputStream = new MemoryStream();
            var encoder = new WebpEncoder
            {
                FileFormat = WebpFileFormatType.Lossy, // サムネイルはLossyで、フルサイズはLosslessで保存する
                Quality = 80 // Lossyモードのときだけ効く
            };
            await image.SaveAsWebpAsync(outputStream, encoder);
            outputStream.Position = 0;

            await thumbBlob.UploadAsync(outputStream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "image/webp" }
            });

            // Cosmos DBに空データ登録
            var workId = Path.GetFileNameWithoutExtension(name);
            await RegisterWorkItemAsync(workId, thumbBlob.Uri.ToString(),
                blobServiceClient.GetBlobContainerClient(FullContainerName)
                    .GetBlobClient($"newFile/{name}").Uri.ToString());
        }

        /// <summary>
        /// CosmosDBにWorkItemを登録する
        /// </summary>
        /// <param name="id"></param>
        /// <param name="thumbnailUrl"></param>
        /// <param name="fullImageUrl"></param>
        /// <returns></returns>
        private async Task RegisterWorkItemAsync(string id, string thumbnailUrl, string fullImageUrl)
        {
            var container = cosmosClient.GetContainer(_databaseId, "Works");

            var workItem = new WorkItem
            {
                Id = id,
                Category = WorkCategory.Unknown,
                Title = string.Empty,
                Description = string.Empty,
                ThumbnailUrl = thumbnailUrl,
                FullImageUrl = fullImageUrl,
                ToolsUsed = ["dummy1", "dummy2"],
                CreatedDate = DateTime.UtcNow,
                SortOrder = 0,
                IsPublished = false
            };

            await container.CreateItemAsync(workItem, new PartitionKey(workItem.Id));
        }

        // CosmosDB登録テスト
        [Function("TestRegisterWorkItem")]
        public async Task TestRegisterWorkItem([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestData req)
        {
            var workId = Guid.NewGuid().ToString();
            await RegisterWorkItemAsync(workId, "https://example.com/thumb.webp", "https://example.com/full.png");
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync($"WorkItem with ID {workId} registered in CosmosDB.");
        }

    }
}
