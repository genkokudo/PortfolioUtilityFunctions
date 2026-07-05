using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace PortfolioUtilityFunctions.Model
{
    public class SkillItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// CosmosDBのPartitionKeyに使うので、必ず小文字で入れる。
        /// </summary>
        [JsonPropertyName("partitionKey")]
        public string PartitionKey { get; set; } = string.Empty;

        /// <summary>
        /// システム開発業務か、デザイン業務か
        /// </summary>
        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 1～5の整数値で評価する。
        /// </summary>
        [JsonPropertyName("rating")]
        public int Rating { get; set; }

        [JsonPropertyName("comment")]
        public string Comment { get; set; } = string.Empty;

        /// <summary>
        /// サイトでの表示順序を指定するための値。小さいほど先に表示される。
        /// </summary>
        [JsonPropertyName("order")]
        public int Order { get; set; }
    }
}
