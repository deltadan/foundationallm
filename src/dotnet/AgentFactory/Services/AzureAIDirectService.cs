﻿using FoundationaLLM.AgentFactory.Core.Interfaces;
using FoundationaLLM.AgentFactory.Core.Models.ConfigurationOptions;
using FoundationaLLM.Common.Constants;
using FoundationaLLM.Common.Exceptions;
using FoundationaLLM.Common.Interfaces;
using FoundationaLLM.Common.Models.Agents;
using FoundationaLLM.Common.Models.Orchestration;
using FoundationaLLM.Common.Settings;
using FoundationaLLM.Prompt.Models.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using FoundationaLLM.Prompt.Constants;
using Azure.ResourceManager.Models;
using static Microsoft.IO.RecyclableMemoryStreamManager;

namespace FoundationaLLM.AgentFactory.Core.Services
{
    /// <summary>
    /// The Azure AI direct orchestration service.
    /// </summary>
    /// <param name="options"></param>
    /// <param name="logger"></param>
    /// <param name="httpClientFactoryService"></param>
    public class AzureAIDirectService(
        IOptions<AzureAIDirectServiceSettings> options,
        ILogger<AzureAIDirectService> logger,
        IHttpClientFactoryService httpClientFactoryService,
        IEnumerable<IResourceProviderService> resourceProviderServices) : IAzureAIDirectService
    {
        readonly AzureAIDirectServiceSettings _settings = options.Value;
        readonly ILogger<AzureAIDirectService> _logger = logger;
        private readonly IHttpClientFactoryService _httpClientFactoryService = httpClientFactoryService;
        readonly JsonSerializerOptions _jsonSerializerOptions = CommonJsonSerializerOptions.GetJsonSerializerOptions();
        readonly Dictionary<string, IResourceProviderService> _resourceProviderServices = resourceProviderServices.ToDictionary(
                rps => rps.Name);

        /// <inheritdoc/>
        public bool IsInitialized => true;

        /// <inheritdoc/>
        public async Task<LLMCompletionResponse> GetCompletion(LLMCompletionRequest request)
        {
            AgentBase? agent = request switch
            {
                KnowledgeManagementCompletionRequest kmcr => kmcr.Agent,
                InternalContextCompletionRequest icr => icr.Agent,
                _ => null
            };
            if (agent == null) throw new Exception("Agent cannot be null.");

            var endpointConfiguration = (agent.OrchestrationSettings?.EndpointConfiguration)
                ?? throw new Exception("Endpoint Configuration must be provided.");
            endpointConfiguration.TryGetValue(EndpointConfigurationKeys.Endpoint, out var endpoint);
            endpointConfiguration.TryGetValue(EndpointConfigurationKeys.APIKey, out var apiKey);

            if (!_resourceProviderServices.TryGetValue(ResourceProviderNames.FoundationaLLM_Prompt, out var promptResourceProvider))
                throw new ResourceProviderException($"The resource provider {ResourceProviderNames.FoundationaLLM_Prompt} was not loaded.");

            MultipartPrompt? prompt = null;
            InputString? systemPrompt = null;
            if (!string.IsNullOrWhiteSpace(agent.PromptObjectId))
            {
                var resourcePath = promptResourceProvider.GetResourcePathFromObjectId(agent.PromptObjectId);
                var resource = await promptResourceProvider.HandleGetAsync(resourcePath);
                if (resource is List<PromptBase> prompts)
                {
                    prompt = prompts.FirstOrDefault() as MultipartPrompt;
                    systemPrompt = new InputString
                    {
                        Role = "system",
                        Content = prompt?.Prefix ?? string.Empty
                    };
                }
            }

            if (endpoint != null && apiKey != null)
            {
                var client = _httpClientFactoryService.CreateClient(HttpClients.AzureAIDirect);
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", apiKey.ToString()
                );
                client.BaseAddress = new Uri(endpoint.ToString()!);
                
                var modelParameters = agent.OrchestrationSettings?.ModelParameters;
                AzureAIDirectRequest azureAIDirectRequest;

                if (modelParameters != null)
                {
                    azureAIDirectRequest = new()
                    {
                        InputData = new()
                        {
                            InputString =
                            [
                                new InputString { Role = "user", Content = request.UserPrompt },
                                systemPrompt
                            ],
                            Parameters = new Parameters
                            {
                                Temperature = Convert.ToSingle(modelParameters.GetValueOrDefault(ModelParameterKeys.Temperature, 0.0f).ToString()),
                                MaxNewTokens = Convert.ToInt32(modelParameters.GetValueOrDefault(ModelParameterKeys.MaxNewTokens, 128).ToString())
                            }
                        }
                    };

                    var body = JsonSerializer.Serialize(azureAIDirectRequest, _jsonSerializerOptions);
                    var content = new StringContent(body, Encoding.UTF8, "application/json");
                    if (modelParameters.TryGetValue(ModelParameterKeys.DeploymentName, out var deployment))
                    {
                        content.Headers.Add("azureml-model-deployment", deployment.ToString());
                    }

                    var responseMessage = await client.PostAsync("", content);
                    var responseContent = await responseMessage.Content.ReadAsStringAsync();

                    if (responseMessage.IsSuccessStatusCode)
                    {
                        var completionResponse = JsonSerializer.Deserialize<AzureAIDirectResponse>(responseContent);

                        return new LLMCompletionResponse
                        {
                            Completion = completionResponse!.Output,
                            UserPrompt = request.UserPrompt,
                            FullPrompt = body,
                            PromptTemplate = systemPrompt?.Content,
                            AgentName = agent.Name,
                            PromptTokens = 0,
                            CompletionTokens = 0
                        };
                    }

                    _logger.LogWarning("The AzureAIDirect orchestration service returned status code {StatusCode}: {ResponseContent}",
                        responseMessage.StatusCode, responseContent);
                }
            }

            return new LLMCompletionResponse
            {
                Completion = "A problem on my side prevented me from responding.",
                UserPrompt = request.UserPrompt,
                PromptTemplate = systemPrompt?.Content,
                AgentName = agent.Name,
                PromptTokens = 0,
                CompletionTokens = 0
            };
        }
    }
}
