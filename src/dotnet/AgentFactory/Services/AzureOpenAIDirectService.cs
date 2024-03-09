﻿using FoundationaLLM.AgentFactory.Core.Interfaces;
using FoundationaLLM.Common.Constants;
using FoundationaLLM.Common.Exceptions;
using FoundationaLLM.Common.Extensions;
using FoundationaLLM.Common.Interfaces;
using FoundationaLLM.Common.Models.Agents;
using FoundationaLLM.Common.Models.Orchestration;
using FoundationaLLM.Common.Models.Orchestration.Direct;
using FoundationaLLM.Common.Settings;
using FoundationaLLM.Prompt.Models.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace FoundationaLLM.AgentFactory.Core.Services
{
    /// <summary>
    /// The Azure OpenAI direct orchestration service.
    /// </summary>
    /// <param name="logger">The logger used for logging.</param>
    /// <param name="configuration">The <see cref="IConfiguration"/> used to retrieve app settings from configuration.</param>
    /// <param name="httpClientFactoryService">The HTTP client factory service.</param>
    /// <param name="resourceProviderServices">A dictionary of <see cref="IResourceProviderService"/> resource providers hashed by resource provider name.</param>
    public class AzureOpenAIDirectService(
        ILogger<AzureOpenAIDirectService> logger,
        IConfiguration configuration,
        IHttpClientFactoryService httpClientFactoryService,
        IEnumerable<IResourceProviderService> resourceProviderServices) : IAzureOpenAIDirectService
    {
        private readonly ILogger<AzureOpenAIDirectService> _logger = logger;
        private readonly IConfiguration _configuration = configuration;
        private readonly IHttpClientFactoryService _httpClientFactoryService = httpClientFactoryService;
        private readonly JsonSerializerOptions _jsonSerializerOptions = CommonJsonSerializerOptions.GetJsonSerializerOptions();
        private readonly Dictionary<string, IResourceProviderService> _resourceProviderServices = resourceProviderServices.ToDictionary(
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

            var endpointSettings = GetEndpointSettings(endpointConfiguration);


            var inputStrings = new List<InputMessage>();
            SystemInputMessage? systemPrompt = null;

            if (endpointSettings.OperationType == OperationTypes.ChatCompletions)
            {
                if (!string.IsNullOrWhiteSpace(agent.PromptObjectId))
                {
                    if (!_resourceProviderServices.TryGetValue(ResourceProviderNames.FoundationaLLM_Prompt, out var promptResourceProvider))
                        throw new ResourceProviderException($"The resource provider {ResourceProviderNames.FoundationaLLM_Prompt} was not loaded.");

                    var resource = await promptResourceProvider.HandleGetAsync(agent.PromptObjectId);
                    if (resource is List<PromptBase> prompts)
                    {
                        var prompt = prompts.FirstOrDefault() as MultipartPrompt;
                        systemPrompt = new SystemInputMessage
                        {
                            Role = InputMessageRoles.System,
                            Content = prompt?.Prefix ?? string.Empty
                        };
                    }
                }

                // Add system prompt, if exists.
                if (systemPrompt != null) inputStrings.Add(systemPrompt);
                // Add conversation history.
                if (agent.ConversationHistory?.Enabled == true && request.MessageHistory?.Count != 0)
                {
                    var messageHistoryItems = request.MessageHistory?.TakeLast(agent.ConversationHistory.MaxHistory);
                    foreach (var item in messageHistoryItems!)
                    {
                        inputStrings.Add(new InputMessage
                        {
                            Role = item.Sender.ToLower(),
                            Content = item.Text
                        });
                    }
                }
                // Add current user prompt.
                var userPrompt = new UserInputMessage { Content = request.UserPrompt };
                inputStrings.Add(userPrompt);
            }

            if (!string.IsNullOrWhiteSpace(endpointSettings.Endpoint) && !string.IsNullOrWhiteSpace(endpointSettings.APIKey))
            {
                var client = _httpClientFactoryService.CreateClient(HttpClients.AzureOpenAIDirect);
                if (endpointSettings.AuthenticationType == "key" && !string.IsNullOrWhiteSpace(endpointSettings.APIKey))
                {
                    client.DefaultRequestHeaders.Add("api-key", endpointSettings.APIKey);
                }
                
                client.BaseAddress = new Uri(endpointSettings.Endpoint);
                
                var modelParameters = agent.OrchestrationSettings?.ModelParameters;
                var modelOverrides = request.Settings?.ModelParameters;

                if (modelParameters != null)
                {
                    var azureOpenAIDirectRequest = endpointSettings.OperationType switch
                    {
                        OperationTypes.Completions => modelParameters.ToObject<AzureOpenAIDirectCompletionRequest>(modelOverrides),
                        OperationTypes.ChatCompletions => modelParameters.ToObject<AzureOpenAIDirectChatCompletionRequest>(modelOverrides),
                        _ => throw new ArgumentException($"The specified operation type {endpointSettings.OperationType} is not supported.")
                    };

                    if (endpointSettings.OperationType == OperationTypes.Completions)
                    {
                        azureOpenAIDirectRequest.Prompt = request.UserPrompt;
                    }
                    else if (endpointSettings.OperationType == OperationTypes.ChatCompletions)
                    {
                        azureOpenAIDirectRequest.Messages = [.. inputStrings];
                    }
                    
                    var body = JsonSerializer.Serialize(azureOpenAIDirectRequest, _jsonSerializerOptions);
                    var content = new StringContent(body, Encoding.UTF8, "application/json");
                    modelParameters.TryGetValue(ModelParameterKeys.DeploymentName, out var deployment);

                    // TODO: the /chat section of the post path will depend on the endpointSettings.OperationType == "chat_completions"
                    var responseMessage = await client.PostAsync($"/openai/deployments/{deployment}/chat/completions?api-version={endpointSettings.APIVersion}", content);
                    var responseContent = await responseMessage.Content.ReadAsStringAsync();

                    if (responseMessage.IsSuccessStatusCode)
                    {
                        var completionResponse = JsonSerializer.Deserialize<AzureOpenAIDirectResponse>(responseContent);

                        return new LLMCompletionResponse
                        {
                            Completion = completionResponse!.Choices?[0].Text,
                            UserPrompt = request.UserPrompt,
                            FullPrompt = body,
                            PromptTemplate = systemPrompt?.Content,
                            AgentName = agent.Name,
                            PromptTokens = 0,
                            CompletionTokens = 0
                        };
                    }

                    _logger.LogWarning("The AzureOpenAIDirect orchestration service returned status code {StatusCode}: {ResponseContent}",
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

        /// <summary>
        /// Extracts endpoint configuration values from a dictionary and writes them into a
        /// <see cref="EndpointSettings"/> object.
        /// </summary>
        /// <param name="endpointConfiguration">Dictionary containing orchestration endpoint configuration values.</param>
        /// <returns>Returns a <see cref="EndpointSettings"/> object containing the endpoint configuration.</returns>
        /// <exception cref="Exception"></exception>
        private EndpointSettings GetEndpointSettings(Dictionary<string, object> endpointConfiguration)
        {
            if (!endpointConfiguration.TryGetValue(EndpointConfigurationKeys.Endpoint, out var endpointKeyName))
                throw new Exception("An endpoint value must be passed in via an Azure App Config key name.");

            var endpoint = _configuration.GetValue<string>(endpointKeyName?.ToString()!);

            var authenticationType = endpointConfiguration.GetValueOrDefault(EndpointConfigurationKeys.AuthenticationType, "key").ToString();
            var apiKey = string.Empty;

            if (authenticationType == "key")
            {
                if (!endpointConfiguration.TryGetValue(EndpointConfigurationKeys.APIKey, out var apiKeyKeyName))
                    throw new Exception("An API key must be passed in via an Azure App Config key name.");

                apiKey = _configuration.GetValue<string>(apiKeyKeyName?.ToString()!)!;
            }

            if (!endpointConfiguration.TryGetValue(EndpointConfigurationKeys.APIVersion, out var apiVersionKeyName))
                throw new Exception("An API version must be passed in via an Azure App Config key name.");

            var apiVersion = _configuration.GetValue<string>(apiVersionKeyName?.ToString()!);

            var operationType = string.Empty;
            if (endpointConfiguration.TryGetValue(EndpointConfigurationKeys.OperationType, out var operationTypeKeyName))
                operationType = _configuration.GetValue<string>(operationTypeKeyName?.ToString()!) ?? OperationTypes.ChatCompletions;

            return new EndpointSettings
            {
                Endpoint = endpoint!,
                APIKey = apiKey!,
                APIVersion = apiVersion!,
                AuthenticationType = authenticationType!,
                OperationType = operationType
            };
        }
    }
}
