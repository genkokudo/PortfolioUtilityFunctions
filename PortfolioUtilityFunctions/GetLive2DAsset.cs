using Azure;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace PortfolioUtilityFunctions;

/// <summary>
/// Privateなlive2dコンテナからモデルの静的資産を配信する。
/// </summary>
public class GetLive2DAsset(BlobServiceClient blobServiceClient)
{
    private const string ContainerName = "live2d";

    [Function("GetLive2DAsset")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "live2d/{model}/{*path}")] HttpRequest req,
        string model,
        string? path)
    {
        // Route値を再デコード・正規化しない。曖昧なパスはStorageへ渡す前に拒否する。
        if (!IsValidSegment(model) || string.IsNullOrEmpty(path)
            || model.Length + 1 + path.Length > 1024
            || !path.Split('/').All(IsValidSegment))
        {
            return new BadRequestObjectResult("Invalid Live2D asset path.");
        }

        var blob = blobServiceClient.GetBlobContainerClient(ContainerName)
            .GetBlobClient($"{model}/{path}");

        try
        {
            var download = await blob.DownloadStreamingAsync(cancellationToken: req.HttpContext.RequestAborted);

            req.HttpContext.Response.Headers.CacheControl = "public, max-age=3600";
            req.HttpContext.Response.Headers.XContentTypeOptions = "nosniff";

            // FileStreamResultが送信完了後にStreamを破棄するため、ここでusingにしない。
            return new FileStreamResult(download.Value.Content, GetContentType(path));
        }
        catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status404NotFound)
        {
            return new NotFoundResult();
        }
    }

    private static bool IsValidSegment(string? segment)
    {
        return !string.IsNullOrWhiteSpace(segment)
            && segment is not "." and not ".."
            && !segment.EndsWith('.')
            && !char.IsWhiteSpace(segment[0])
            && !char.IsWhiteSpace(segment[^1])
            && !segment.Any(c => char.IsControl(c) || c is '/' or '\\' or '%' or ':' or '?' or '#');
    }

    private static string GetContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" => "application/json",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".wav" => "audio/wav",
        ".mp3" => "audio/mpeg",
        ".ogg" => "audio/ogg",
        _ => "application/octet-stream" // .moc3を含むバイナリ・未知の形式
    };
}
