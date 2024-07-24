﻿using FoundationaLLM.Common.Authentication;
using FoundationaLLM.Common.Constants.Authentication;
using FoundationaLLM.Common.Constants.Configuration;
using FoundationaLLM.Common.Constants.ResourceProviders;
using FoundationaLLM.Common.Exceptions;
using FoundationaLLM.Common.Extensions;
using FoundationaLLM.Common.Interfaces;
using FoundationaLLM.Common.Models.Configuration.API;
using FoundationaLLM.Common.Models.Infrastructure;
using FoundationaLLM.Common.Models.ResourceProviders.Configuration;
using FoundationaLLM.Orchestration.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FoundationaLLM.Orchestration.Core.Services
{
    /// <summary>
    /// Manages internal and external orchestration services.
    /// </summary>
    public class LLMOrchestrationServiceManager : ILLMOrchestrationServiceManager
    {
        private readonly Dictionary<string, IResourceProviderService> _resourceProviderServices;
        private readonly IConfiguration _configuration;
        private readonly ILogger<LLMOrchestrationServiceManager> _logger;

        private Dictionary<string, APISettingsBase> _externalOrchestrationServiceSettings = [];
        private bool _initialized = false;

        /// <summary>
        /// Creates a new instance of the LLM Orchestration Service Manager.
        /// </summary>
        /// <param name="resourceProviderServices">A list of <see cref="IResourceProviderService"/> resource providers.</param>
        /// <param name="configuration">The <see cref="IConfiguration"/> used to retrieve configuration values.</param>
        /// <param name="logger">The logger for the orchestration service manager.</param>
        public LLMOrchestrationServiceManager(
            IEnumerable<IResourceProviderService> resourceProviderServices,
            IConfiguration configuration,
            ILogger<LLMOrchestrationServiceManager> logger)
        {
            _resourceProviderServices =
                resourceProviderServices.ToDictionary<IResourceProviderService, string>(rps => rps.Name);
            _configuration = configuration;
            _logger = logger;

            // Kicks off the initialization on a separate thread and does not wait for it to complete.
            // The completion of the initialization process will be signaled by setting the _initialized property.
            _ = Task.Run(Initialize);
        }

        #region Initialization

        /// <summary>
        /// Performs the initialization of the orchestration service.
        /// </summary>
        /// <returns></returns>
        private async Task Initialize()
        {
            try
            {
                _logger.LogInformation("Starting to initialize the LLM Orchestration Service Manager service...");

                var configurationResourceProvider = _resourceProviderServices[ResourceProviderNames.FoundationaLLM_Configuration];
                await configurationResourceProvider.WaitForInitialization();

                var apiEndpoint = await configurationResourceProvider.GetResources<APIEndpointConfiguration>(
                    DefaultAuthentication.ServiceIdentity!);

                _externalOrchestrationServiceSettings = apiEndpoint
                    .Where(ae => ae.Category == APIEndpointCategory.ExternalOrchestration
                        && ae.AuthenticationParameters.TryGetValue(AuthenticationParameterKeys.APIKeyConfigurationName, out var apiKeyConfigObj)
                        && apiKeyConfigObj is string apiKeyConfig
                        && !string.IsNullOrWhiteSpace(apiKeyConfig)
                        && apiKeyConfig.StartsWith(AppConfigurationKeySections.FoundationaLLM_ExternalAPIs))
                    .Where(eos => eos.APIKeyConfigurationName is not null &&  eos.APIKeyConfigurationName.StartsWith(AppConfigurationKeySections.FoundationaLLM_ExternalAPIs))
                    .ToDictionary(
                        ae => ae.Name,
                        ae => new APISettingsBase
                        {
                            APIKey = _configuration[ae.AuthenticationParameters[AuthenticationParameterKeys.APIKeyConfigurationName].ToString()!],
                            APIUrl = ae.Url
                        });

                _initialized = true;

                _logger.LogInformation("The LLM Orchestration Service Manager service was successfully initialized.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing the LLM Orchestration Service Manager service.");
            }
        }

        #endregion

        /// <inheritdoc/>
        public async Task<List<ServiceStatusInfo>> GetAggregateStatus(string instanceId, IServiceProvider serviceProvider)
        {
            var result = new List<ServiceStatusInfo>();

            var serviceStatuses = GetOrchestrationServices(serviceProvider)
                .ToAsyncEnumerable()
                .SelectAwait(async x => await x.GetStatus(instanceId));

            await foreach (var serviceStatus in serviceStatuses)
                result.Add(serviceStatus);

            return result;
        }

        /// <inheritdoc/>
        public ILLMOrchestrationService GetService(string instanceId, string serviceName, IServiceProvider serviceProvider, ICallContext callContext)
        {
            var internalOrchestrationService = serviceProvider.GetServices<ILLMOrchestrationService>()
                .SingleOrDefault(srv => srv.Name == serviceName);

            if (internalOrchestrationService != null)
                return internalOrchestrationService;

            if (_externalOrchestrationServiceSettings.TryGetValue(serviceName, out var externalOrchestrationServiceSettings))
                return new LLMOrchestrationService(
                    serviceName,
                    Options.Create<APISettingsBase>(externalOrchestrationServiceSettings),
                    serviceProvider.GetRequiredService<ILogger<LLMOrchestrationService>>(),
                    serviceProvider.GetRequiredService<IHttpClientFactory>(),
                    callContext);

            throw new OrchestrationException($"The LLM orchestration service {serviceName} is not available.");
        }

        private IEnumerable<ILLMOrchestrationService> GetOrchestrationServices(IServiceProvider serviceProvider) =>
            serviceProvider.GetServices<ILLMOrchestrationService>()
                .Where(llmSrv =>
                    llmSrv.GetType() == typeof(LangChainService)
                    || llmSrv.GetType() == typeof(SemanticKernelService))
                .Concat(
                    _externalOrchestrationServiceSettings.Select(eos =>
                        new LLMOrchestrationService(
                            eos.Key,
                            Options.Create<APISettingsBase>(eos.Value),
                            serviceProvider.GetRequiredService<ILogger<LLMOrchestrationService>>(),
                            serviceProvider.GetRequiredService<IHttpClientFactory>(),
                            serviceProvider.GetRequiredService<ICallContext>())));
    }
}
