﻿using System.Text;
using System.Text.Json;
using FoundationaLLM.Common.Interfaces;
using FoundationaLLM.Common.Models.Orchestration;
using FoundationaLLM.Common.Settings;
using Microsoft.Extensions.Logging;

namespace FoundationaLLM.Common.Services.API
{
    /// <summary>
    /// Contains methods for interacting with the downstream API.
    /// </summary>
    /// <param name="downstreamHttpClientName">The name of the downstream HTTP client.</param>
    /// <param name="httpClientFactoryService">The HTTP client factory service.</param>
    /// <param name="logger">The <see cref="ILogger"/> used for logging.</param>
    public class DownstreamAPIService(
        string downstreamHttpClientName,
        IHttpClientFactoryService httpClientFactoryService,
        ILogger<DownstreamAPIService> logger) : IDownstreamAPIService
    {
        private readonly string _downstreamHttpClientName = downstreamHttpClientName;
        private readonly IHttpClientFactoryService _httpClientFactoryService = httpClientFactoryService;
        private readonly JsonSerializerOptions _jsonSerializerOptions = CommonJsonSerializerOptions.GetJsonSerializerOptions();
        private readonly ILogger<DownstreamAPIService> _logger = logger;

        /// <summary>
        /// <inheritdoc/>
        /// </summary>
        public string APIName => _downstreamHttpClientName;

        /// <inheritdoc/>
        public async Task<CompletionResponse> GetCompletion(CompletionRequest completionRequest)
        {
            var fallback = new CompletionResponse
            {
                Completion = "A problem on my side prevented me from responding.",
                UserPrompt = completionRequest.UserPrompt ?? string.Empty,
                PromptTokens = 0,
                CompletionTokens = 0,
                UserPromptEmbedding = [ 0f ]
            };

            var client = await _httpClientFactoryService.CreateClient(_downstreamHttpClientName);

            _logger.LogInformation(
                "Created Http client {ClientName} with timeout {Timeout} seconds.",
                _downstreamHttpClientName,
                (int)client.Timeout.TotalSeconds);

            var serializedRequest = JsonSerializer.Serialize(completionRequest, _jsonSerializerOptions);
            var responseMessage = await client.PostAsync("orchestration/completion",
                new StringContent(
                    serializedRequest,
                        Encoding.UTF8, "application/json"));

            _logger.LogInformation(
                "Http client {ClientName} returned a response with status code {HttpStatusCode}.",
                _downstreamHttpClientName,
                responseMessage.StatusCode);

            if (responseMessage.IsSuccessStatusCode)
            {
                var responseContent = await responseMessage.Content.ReadAsStringAsync();
                var completionResponse = JsonSerializer.Deserialize<CompletionResponse>(responseContent);

                return completionResponse ?? fallback;
            }

            return fallback;
        }

        /// <inheritdoc/>
        public async Task<SummaryResponse> GetSummary(SummaryRequest summaryRequest)
        {
            var fallback = new SummaryResponse
            {
                Summary = "[No Summary]"
            };

            var client = await _httpClientFactoryService.CreateClient(_downstreamHttpClientName);

            var responseMessage = await client.PostAsync("orchestration/summary",
            new StringContent(
                    JsonSerializer.Serialize(summaryRequest, _jsonSerializerOptions),
                    Encoding.UTF8, "application/json"));

            if (responseMessage.IsSuccessStatusCode)
            {
                var responseContent = await responseMessage.Content.ReadAsStringAsync();
                var summarizeResponse = JsonSerializer.Deserialize<SummaryResponse>(responseContent);

                return summarizeResponse ?? fallback;
            }

            return fallback;
        }
    }
}
