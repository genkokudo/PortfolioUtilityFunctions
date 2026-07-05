using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using PortfolioUtilityFunctions.Model;
using System;
using System.Collections.Generic;
using System.Text;

namespace PortfolioUtilityFunctions
{
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
            var skills = await GetSkillsAsync();

            var result = new Dictionary<string, object>
            {
                ["Skills"] = skills,
                //["WorkHistory"] = workHistory  // 後で追加するだけ
            };

            return new OkObjectResult(result);
        }

        /// <summary>
        /// Skillsを取得する
        /// </summary>
        /// <returns>Skillのリスト</returns>
        private async Task<List<SkillItem>> GetSkillsAsync()
        {
            var container = cosmosClient.GetContainer(_databaseId, "Skills");

            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.partitionKey = @pk ORDER BY c.order")
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
    }
}
