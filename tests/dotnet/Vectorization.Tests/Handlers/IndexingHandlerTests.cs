﻿using FakeItEasy;
using FoundationaLLM.Common.Interfaces;
using FoundationaLLM.Common.Models.TextEmbedding;
using FoundationaLLM.Vectorization.Handlers;
using FoundationaLLM.Vectorization.Interfaces;
using FoundationaLLM.Vectorization.Models;
using FoundationaLLM.Vectorization.Models.Resources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Vectorization.Tests
{
    internal class IndexingMockService : IIndexingService
    {
        public static EmbeddedContent IndexEmbeddingsAsyncEmbeddedContentArgument { get; private set; }
        public static string IndexEmbeddingsAsyncIndexNameArgument { get; private set; }

        public Task<List<string>> IndexEmbeddingsAsync(EmbeddedContent embeddedContent, string indexName)
        {
            IndexEmbeddingsAsyncEmbeddedContentArgument = embeddedContent;
            IndexEmbeddingsAsyncIndexNameArgument = indexName;
            return Task.FromResult(new List<string>());
        }
    }

    internal class IndexingMockServiceFactory : IVectorizationServiceFactory<IIndexingService>
    {
        IIndexingService IVectorizationServiceFactory<IIndexingService>.GetService(string serviceName)
        {
            throw new NotImplementedException();
        }

        (IIndexingService Service, VectorizationProfileBase VectorizationProfile) IVectorizationServiceFactory<IIndexingService>.GetServiceWithProfile(string serviceName)
        {
            return (
                new IndexingMockService(),
                new VectorizationProfileBase {
                    Name = "IndexingMockService",
                    Settings = new Dictionary<string, string> {
                        { "IndexName", "test-001-index" } 
                    } 
                }
            );
        }
    }

    public class IndexingHandlerTests
    {
        [Fact]
        public async void TestProcessRequest()
        {
            IConfigurationRoot configurationRoot = new ConfigurationBuilder().Build();
            IConfigurationSection stepsConfiguration = configurationRoot.GetSection("");

            // DI container configuration
            IVectorizationStateService stateService = A.Fake<IVectorizationStateService>();
            IServiceCollection serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton<IVectorizationServiceFactory<IIndexingService>, IndexingMockServiceFactory>();

            ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

            IndexingHandler handler = new IndexingHandler(
                "Queue-Message-1",
                new Dictionary<string, string> { { "indexing_profile_name", "" } },
                stepsConfiguration,
                stateService,
                serviceCollection.BuildServiceProvider(),
                loggerFactory
            );

            ContentIdentifier contentIdentifier = new ContentIdentifier
            {
                MultipartId = new List<string> {
                    "https://somesa.blob.core.windows.net",
                    "vectorization-input",
                    "somedata.pdf"
                },
                ContentSourceProfileName = "SomePDFData",
                CanonicalId = "SomeBusinessUnit/SomePDFData"
            };
            VectorizationRequest request = new VectorizationRequest
            {
                Id = "d4669c9c-e330-450a-a41c-a4d6649abdef",
                ContentIdentifier = contentIdentifier,
                ProcessingType = VectorizationProcessingType.Synchronous,
                Steps = new List<VectorizationStep>
                {
                    new VectorizationStep { Id = "index", Parameters = new Dictionary<string, string> { } }
                },
                CompletedSteps = new List<string> { },
                RemainingSteps = new List<string> { "index" }
            };
            VectorizationState state = new VectorizationState
            {
                CurrentRequestId = "d4669c9c-e330-450a-a41c-a4d6649abdef",
                ContentIdentifier = contentIdentifier,
                Artifacts = new List<VectorizationArtifact> {}
            };
            CancellationTokenSource tokenSource = new CancellationTokenSource();

            // No text embedding artifacts
            Assert.False(await handler.Invoke(request, state, tokenSource.Token));

            Embedding sampleEmbedding = new Embedding();

            state.Artifacts.Add(
                new VectorizationArtifact
                {
                    Type = VectorizationArtifactType.TextEmbeddingVector,
                    Position = 1,
                    Content = JsonSerializer.Serialize(
                        sampleEmbedding,
                        new JsonSerializerOptions { Converters = { new Embedding.JsonConverter() } }
                    )
                }
            );

            // No text partition artifacts
            Assert.False(await handler.Invoke(request, state, tokenSource.Token));

            // Even though there are two partitions, only one embedding vector should be indexed
            state.Artifacts.Add(new VectorizationArtifact { Type = VectorizationArtifactType.TextPartition, Position = 2, Content = "This is the first line in a paragraph." });
            state.Artifacts.Add(new VectorizationArtifact { Type = VectorizationArtifactType.TextPartition, Position = 3, Content = "This is the second line in a paragraph." });

            await handler.Invoke(request, state, tokenSource.Token);

            Assert.True(IndexingMockService.IndexEmbeddingsAsyncEmbeddedContentArgument.ContentParts.Count == 1);
            EmbeddedContentPart embeddedContentPart = IndexingMockService.IndexEmbeddingsAsyncEmbeddedContentArgument.ContentParts[0];
            Assert.Equal("https://somesa.blob.core.windows.net/vectorization-input/somedata.pdf#000002", embeddedContentPart.Id);
        }
    }
}