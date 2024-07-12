﻿using FoundationaLLM.Common.Interfaces;
using FoundationaLLM.Common.Models.Authentication;
using FoundationaLLM.Common.Services;
using NSubstitute;
using System.Text.Json;

namespace FoundationaLLM.Common.Tests.Services
{
    public class HttpClientFactoryServiceTests
    {
        private readonly IEnumerable<IResourceProviderService> _resourceProviderServices;
        private readonly IHttpClientFactory _httpClientFactory;

        public HttpClientFactoryServiceTests()
        {
            _resourceProviderServices = Substitute.For<IEnumerable<IResourceProviderService>>();
            _httpClientFactory = Substitute.For<IHttpClientFactory>();
        }

        [Fact]
        public async void CreateClient_WithValidClientName_ReturnsHttpClientWithHeaders()
        {
            // Arrange
            var clientName = "TestClient";
            var apiKey = "TestAPIKey";
            var userContext = new UnifiedUserIdentity
            {
                UPN = "TestUPN",
                Username = "TestUsername",
                Name = "TestName"
            };

            var httpClient = new HttpClient();
            _httpClientFactory.CreateClient(clientName).Returns(httpClient);

            var service = new HttpClientFactoryService(_resourceProviderServices, _httpClientFactory);

            // Act
            var result = await service.CreateClient(clientName, userContext);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(httpClient.Timeout, TimeSpan.FromSeconds(600));
            Assert.Equal(result.DefaultRequestHeaders.GetValues(Constants.HttpHeaders.APIKey), new[] { apiKey });
            Assert.Equal(result.DefaultRequestHeaders.GetValues(Constants.HttpHeaders.UserIdentity), new[] { JsonSerializer.Serialize(userContext) });
        }
    }
}
