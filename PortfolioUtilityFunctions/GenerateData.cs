using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Portfolio.Shared.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace PortfolioUtilityFunctions
{
    /// <summary>
    /// データを生成する関数
    /// </summary>
    /// <param name="cosmosClient"></param>
    public class GenerateData(CosmosClient cosmosClient, BlobServiceClient blobServiceClient)
    {
        private readonly Size BannerThumbSize = new(280, 280);  // 正方形ならバナー、長方形ならフライヤーとする。
        private readonly Size FlyerThumbSize = new(248, 350);

        [Function("GenerateThumbnail")]
        public async Task Run(
            [BlobTrigger("works-full/{name}", Connection = "StorageConnection")] Stream inputBlob,
            string name,
            FunctionContext context)
        {
            var logger = context.GetLogger("GenerateThumbnail");
            logger.LogInformation($"サムネイル生成開始: {name}");

            // 画像を読み込んで、サイズを既定のサムネイルサイズに縮小する
            using var image = await Image.LoadAsync(inputBlob);
            var thumbSize = image.Size.Width == image.Size.Height ? BannerThumbSize : FlyerThumbSize; 
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = thumbSize
            }));

            // Blob Storageにサムネイルを保存する
            var thumbBlob = blobServiceClient.GetBlobContainerClient("works-thumb").GetBlobClient(name);    // works-full（フルサイズ原本）, works-thumb(サムネイル)

            using var outputStream = new MemoryStream();
            var encoder = new WebpEncoder
            {
                FileFormat = WebpFileFormatType.Lossy, // サムネイルはLossyで、フルサイズはLosslessで保存する
                Quality = 80 // Lossyモードのときだけ効く
            };
            await image.SaveAsWebpAsync(outputStream, encoder);
            outputStream.Position = 0;
            await thumbBlob.UploadAsync(outputStream, overwrite: true);

            // ここでCosmosDBのドキュメント更新も呼び出すと良い
            
        }
    }
}
