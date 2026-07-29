using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Portfolio.Shared.Model;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace PortfolioUtilityFunctions
{
    // Blazor WebAssembly (WASM)は環境変数も解析で読まれる可能性があるので、AnonymousにしてCORSでドメイン制限をかける。WASMからアクセスする関数は、関数キー方式は使わない。
    // ただしCORSによる制限は「許可されてないサイトのJSからは叩けない」だけなのでブラウザからだったら普通にアクセスできる。
    // つまりWASMからアクセスする関数は、誰かに叩かれても困らない処理でなければならない。

    /// <summary>
    /// ポートフォリオのデータを取得する関数
    /// </summary>
    /// <param name="cosmosClient"></param>
    public class GetPortfolioData(CosmosClient cosmosClient)
    {
        private readonly string _databaseId = "PortfolioDB";

        /// <summary>
        /// ポートフォリオデータを取得するHTTP関数
        /// </summary>
        /// <param name="req">特になし</param>
        /// <returns></returns>
        [Function("GetPortfolioData")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req)
        {
            var profile = await GetProfileAsync();
            var result = new PortfolioData
            {
                AuthorNameKanji = profile?.AuthorNameKanji ?? string.Empty,
                AuthorNameHiragana = profile?.AuthorNameHiragana ?? string.Empty,
                AuthorNameAlphabet = profile?.AuthorNameAlphabet ?? string.Empty,
                ProfileImageUrl = profile?.ProfileImageUrl ?? string.Empty,
                ProfessionalTitle = profile?.ProfessionalTitle ?? string.Empty,
                HeroTitle = profile?.HeroTitle ?? string.Empty,
                ProfileDescription = profile?.ProfileDescription ?? string.Empty,
                GitHubUrl = profile?.GitHubUrl ?? string.Empty,
                ProfileTags = profile?.ProfileTags ?? new List<string>(),
                Skills = await GetSkillsAsync(),
                WorkHistories = await GetWorkHistoriesAsync(),
                Works = await GetWorksAsync()
            };

            return new OkObjectResult(result);
        }

        /// <summary>
        /// Profileを取得する
        /// </summary>
        /// <returns></returns>
        private async Task<ProfileItem?> GetProfileAsync()
        {
            var container = cosmosClient.GetContainer(_databaseId, "Profile");
            try
            {
                var response = await container.ReadItemAsync<ProfileItem>("profile", new PartitionKey("profile"));
                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null; // ドキュメントがまだ無い場合
            }
        }

        /// <summary>
        /// Skillsを取得する
        /// </summary>
        /// <returns>Skillのリスト</returns>
        private async Task<List<SkillItem>> GetSkillsAsync()
        {
            var container = cosmosClient.GetContainer(_databaseId, "Skills");

            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.partitionKey = @pk ORDER BY c.sortOrder")
                .WithParameter("@pk", "skill");

            var results = new List<SkillItem>();
            var iterator = container.GetItemQueryIterator<SkillItem>(query);

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                results.AddRange(response);
            }

            return results;
        }

        /// <summary>
        /// WorkHistoriesを取得する
        /// </summary>
        /// <returns>WorkHistoryのリスト</returns>
        private async Task<List<WorkHistoryItem>> GetWorkHistoriesAsync()
        {
            var container = cosmosClient.GetContainer(_databaseId, "WorkHistory");

            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.partitionKey = @pk ORDER BY c.sortOrder")
                .WithParameter("@pk", "workHistory");

            var results = new List<WorkHistoryItem>();
            var iterator = container.GetItemQueryIterator<WorkHistoryItem>(query);

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                results.AddRange(response);
            }

            return results;
        }

        /// <summary>
        /// Worksを取得する
        /// </summary>
        /// <returns>WorkItemのリスト</returns>
        private async Task<List<WorkItem>> GetWorksAsync()
        {
            var container = cosmosClient.GetContainer(_databaseId, "Works");
            var query = container.GetItemQueryIterator<WorkItem>(
                new QueryDefinition(
                    "SELECT * FROM c WHERE c.isPublished = true ORDER BY c.sortOrder"));

            var results = new List<WorkItem>();
            while (query.HasMoreResults)
            {
                var response = await query.ReadNextAsync();
                results.AddRange(response);
            }

            return results;
        }

        // CosmosDB読み込みテスト
        [Function("TestReadWorkItem")]
        public async Task<IActionResult> TestReadWorkItem(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req)
        {
            // "Works"コンテナからデータを取得する
            var container = cosmosClient.GetContainer(_databaseId, "Works");
            var query = new QueryDefinition("SELECT * FROM c");
            var iterator = container.GetItemQueryIterator<WorkItem>(query);
            var workItems = new List<WorkItem>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                workItems.AddRange(response);
            }

            return new OkObjectResult(workItems);
        }
    }
}
