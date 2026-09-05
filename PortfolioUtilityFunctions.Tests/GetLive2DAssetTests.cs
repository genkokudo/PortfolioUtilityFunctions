using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace PortfolioUtilityFunctions.Tests;

public class GetLive2DAssetTests
{
    [Theory]
    [InlineData("1254.model3.json", "application/json")]
    [InlineData("1254.physics3.json", "application/json")]
    [InlineData("1254cdi3.json", "application/json")]
    [InlineData("1254.moc3", "application/octet-stream")]
    [InlineData("textures/texture_00.png", "image/png")]
    [InlineData("textures/texture_00.PNG", "image/png")]
    [InlineData("textures/test.jpeg", "image/jpeg")]
    [InlineData("textures/test.webp", "image/webp")]
    [InlineData("sounds/test.wav", "audio/wav")]
    [InlineData("sounds/test.mp3", "audio/mpeg")]
    [InlineData("sounds/test.ogg", "audio/ogg")]
    [InlineData("unknown.bin", "application/octet-stream")]
    [InlineData("日本語/モデル.json", "application/json")]
    public async Task ReturnsExactBlobWithContentTypeAndCache(string path, string contentType)
    {
        var client = new TestServiceClient();
        var context = new DefaultHttpContext();
        using var cancellation = new CancellationTokenSource();
        context.RequestAborted = cancellation.Token;

        var result = Assert.IsType<FileStreamResult>(await new GetLive2DAsset(client).Run(context.Request, "penguin", path));

        Assert.Equal("live2d", client.ContainerName);
        Assert.Equal($"penguin/{path}", client.Container.BlobName);
        Assert.Equal(contentType, result.ContentType);
        Assert.Equal("public, max-age=3600", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions.ToString());
        Assert.Equal(cancellation.Token, client.Container.Blob.CancellationToken);
        Assert.True(result.FileStream.CanRead);
        using var output = new MemoryStream();
        await result.FileStream.CopyToAsync(output);
        Assert.Equal(new byte[] { 0, 1, 128, 255 }, output.ToArray());
        await result.FileStream.DisposeAsync();
    }

    [Theory]
    [InlineData("", "1254.moc3")]
    [InlineData("..", "1254.moc3")]
    [InlineData("penguin/other", "1254.moc3")]
    [InlineData("%2e%2e", "1254.moc3")]
    [InlineData("penguin", null)]
    [InlineData("penguin", "")]
    [InlineData("penguin", "/1254.moc3")]
    [InlineData("penguin", "textures/")]
    [InlineData("penguin", "textures//file.png")]
    [InlineData("penguin", "../other/file.png")]
    [InlineData("penguin", "textures/../file.png")]
    [InlineData("penguin", "./file.png")]
    [InlineData("penguin", "textures\\file.png")]
    [InlineData("penguin", "%2e%2e/file.png")]
    [InlineData("penguin", "%252e%252e/file.png")]
    [InlineData("penguin", "textures%2ffile.png")]
    [InlineData("penguin", "file.png\0")]
    [InlineData("penguin", "file\r\n.png")]
    [InlineData("penguin", "file.png?key=value")]
    [InlineData("penguin", "file.png#fragment")]
    [InlineData("penguin", "C:/file.png")]
    [InlineData("penguin", "textures /file.png")]
    [InlineData("penguin", "textures./file.png")]
    public async Task RejectsInvalidPathsBeforeStorageAccess(string model, string? path)
    {
        var client = new TestServiceClient();
        var context = new DefaultHttpContext();
        Assert.IsType<BadRequestObjectResult>(await new GetLive2DAsset(client).Run(context.Request, model, path));
        Assert.Null(client.ContainerName);
        Assert.False(context.Response.Headers.ContainsKey("Cache-Control"));
    }

    [Fact]
    public async Task RejectsOversizedBlobName()
    {
        var client = new TestServiceClient();
        Assert.IsType<BadRequestObjectResult>(await new GetLive2DAsset(client).Run(new DefaultHttpContext().Request, "penguin", new string('a', 1024)));
        Assert.Null(client.ContainerName);
    }

    [Theory]
    [InlineData("BlobNotFound")]
    [InlineData("ContainerNotFound")]
    public async Task MissingBlobOrContainerReturns404WithoutCache(string errorCode)
    {
        var client = new TestServiceClient();
        client.Container.Blob.Error = new RequestFailedException(404, "Missing", errorCode, null);
        var context = new DefaultHttpContext();
        Assert.IsType<NotFoundResult>(await new GetLive2DAsset(client).Run(context.Request, "penguin", "missing.moc3"));
        Assert.False(context.Response.Headers.ContainsKey("Cache-Control"));
    }

    [Theory]
    [InlineData(403)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task StorageFailuresAreNotDisguisedAs404(int status)
    {
        var client = new TestServiceClient();
        client.Container.Blob.Error = new RequestFailedException(status, "Storage failure");
        var context = new DefaultHttpContext();
        await Assert.ThrowsAsync<RequestFailedException>(() => new GetLive2DAsset(client).Run(context.Request, "penguin", "1254.moc3"));
        Assert.False(context.Response.Headers.ContainsKey("Cache-Control"));
    }

    [Fact]
    public async Task PropagatesRequestCancellation()
    {
        var client = new TestServiceClient();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = new DefaultHttpContext { RequestAborted = cancellation.Token };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GetLive2DAsset(client).Run(context.Request, "penguin", "1254.moc3"));
    }

    private sealed class TestServiceClient : BlobServiceClient
    {
        public string? ContainerName { get; private set; }
        public TestContainerClient Container { get; } = new();
        public override BlobContainerClient GetBlobContainerClient(string blobContainerName)
        {
            ContainerName = blobContainerName;
            return Container;
        }
    }

    private sealed class TestContainerClient : BlobContainerClient
    {
        public string? BlobName { get; private set; }
        public TestBlobClient Blob { get; } = new();
        public override BlobClient GetBlobClient(string blobName)
        {
            BlobName = blobName;
            return Blob;
        }
    }

    private sealed class TestBlobClient : BlobClient
    {
        public Exception? Error { get; set; }
        public CancellationToken CancellationToken { get; private set; }
        public override Task<Response<BlobDownloadStreamingResult>> DownloadStreamingAsync(BlobDownloadOptions? options = null, CancellationToken cancellationToken = default)
        {
            CancellationToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            if (Error is not null) throw Error;
            var download = BlobsModelFactory.BlobDownloadStreamingResult(new MemoryStream([0, 1, 128, 255]));
            return Task.FromResult(Response.FromValue(download, null!));
        }
    }
}
