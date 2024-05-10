﻿using FoundationaLLM.Common.Models.ResourceProviders.Vectorization;

namespace FoundationaLLM.Core.Examples.Catalogs
{
    public static class IndexingProfilesCatalog
    {
        public static readonly List<IndexingProfile> Items =
        [
            new IndexingProfile { Name = "indexing_profile_really_big", Indexer = IndexerType.AzureAISearchIndexer, Settings = new Dictionary<string, string>{ { "IndexName", "reallybig" }, { "TopN", "3" }, { "Filters", "" }, { "EmbeddingFieldName", "Embedding" }, { "TextFieldName", "Text" } }, ConfigurationReferences = new Dictionary<string, string>{ { "AuthenticationType", "FoundationaLLM:Vectorization:AzureAISearchIndexingService:AuthenticationType" }, { "Endpoint", "FoundationaLLM:Vectorization:AzureAISearchIndexingService:Endpoint" } } },
            new IndexingProfile { Name = "indexing_profile_pdf_datalake", Indexer = IndexerType.AzureAISearchIndexer, Settings = new Dictionary<string, string>{ { "IndexName", "pdfdatalake" }, { "TopN", "3" }, { "Filters", "" }, { "EmbeddingFieldName", "Embedding" }, { "TextFieldName", "Text" } }, ConfigurationReferences = new Dictionary<string, string>{ { "AuthenticationType", "FoundationaLLM:Vectorization:AzureAISearchIndexingService:AuthenticationType" }, { "Endpoint", "FoundationaLLM:Vectorization:AzureAISearchIndexingService:Endpoint" } } }
        ];

        public static List<IndexingProfile> GetIndexingProfiles()
        {
            var items = new List<IndexingProfile>();
            items.AddRange(Items);
            return items;
        }
    }
}
