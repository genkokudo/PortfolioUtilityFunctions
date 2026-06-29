using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace PortfolioUtilityFunctions;

public class Function1(ILogger<Function1> logger)
{
    // Blazor WebAssembly (WASM)は環境変数も解析で読まれる可能性があるので、AnonymousにしてCORSでドメイン制限をかける。WASMからアクセスする関数は、関数キー方式は使わない。
    [Function("TestFromSite")]
    public IActionResult RunTest([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req)
    {
        logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions1!🐧");
    }

    [Function("TestFromBrowser")]
    public IActionResult RunTest2([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions2!🐧");
    }
}