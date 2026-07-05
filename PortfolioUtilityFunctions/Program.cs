using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// CosmosDBに接続
var cosmosConnectionString = Environment.GetEnvironmentVariable("CosmosDB__ConnectionString");
builder.Services.AddSingleton(new CosmosClient(cosmosConnectionString));

builder.Build().Run();
