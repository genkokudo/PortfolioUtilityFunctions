using Azure.Storage.Blobs;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
// Cosmos DB SDK（Microsoft.Azure.Cosmos）は、Newtonsoft.Jsonを内部シリアライザーとして使っている。
builder.Services.AddSingleton(sp =>
{
    var connectionString = Environment.GetEnvironmentVariable("CosmosDB__ConnectionString");
    var options = new CosmosClientOptions
    {
        SerializerOptions = new CosmosSerializationOptions
        {
            PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
        }
    };
    return new CosmosClient(connectionString, options);
});

builder.Build().Run();
