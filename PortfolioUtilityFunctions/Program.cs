using Azure.Storage.Blobs;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.Json;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights()
    .AddAzureClients(clientBuilder =>
    {
        // Blob Storageに接続
        clientBuilder.AddBlobServiceClient(Environment.GetEnvironmentVariable("StorageConnection"));
    })
;

// CosmosDBに接続
// CosmosClientをSystem.Text.Json使うように設定変更する
// Cosmos DB SDK（Microsoft.Azure.Cosmos）は、Newtonsoft.Jsonを内部シリアライザーとして使っているため、System.Text.Jsonを使うように変更する必要がある。
builder.Services.AddSingleton(sp =>
{
    var connectionString = Environment.GetEnvironmentVariable("CosmosDB__ConnectionString");
    var options = new CosmosClientOptions
    {
        UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }
    };
    return new CosmosClient(connectionString, options);
});

builder.Build().Run();
