﻿using FoundationaLLM.Authorization.Models.Configuration;
using FoundationaLLM.Common.Authentication;
using FoundationaLLM.Common.Interfaces;
using FoundationaLLM.Common.Models.Authorization;
using FoundationaLLM.Common.Models.ResourceProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FoundationaLLM.Authorization.Services
{
    /// <summary>
    /// Provides methods for interacting with the Authorization API.
    /// </summary>
    public class AuthorizationService : IAuthorizationService
    {
        private readonly AuthorizationServiceSettings _settings;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<AuthorizationService> _logger;

        public AuthorizationService(
            IHttpClientFactory httpClientFactory,
            IOptions<AuthorizationServiceSettings> options,
            ILogger<AuthorizationService> logger)
        {
            _settings = options.Value;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<ActionAuthorizationResult> ProcessAuthorizationRequest(
            string instanceId,
            ActionAuthorizationRequest authorizationRequest)
        {
            var authorizationResults = authorizationRequest.ResourcePaths.Distinct().ToDictionary(rp => rp, auth => false);

            try
            {
                var httpClient = await CreateHttpClient();
                var response = await httpClient.PostAsync(
                    $"/instances/{instanceId}/authorize",
                    JsonContent.Create(authorizationRequest));

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<ActionAuthorizationResult>(responseContent)!;
                }

                _logger.LogError("The call to the Authorization API returned an error: {StatusCode} - {ReasonPhrase}.", response.StatusCode, response.ReasonPhrase);
                return new ActionAuthorizationResult { AuthorizationResults = authorizationResults };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "There was an error calling the Authorization API");
                return new ActionAuthorizationResult { AuthorizationResults = authorizationResults };
            }
        }

        /// <inheritdoc/>
        public async Task<RoleAssignmentResult> ProcessRoleAssignmentRequest(string instanceId, RoleAssignmentRequest roleAssignmentRequest)
        {
            try
            {
                var httpClient = await CreateHttpClient();
                var response = await httpClient.PostAsync(
                    $"/instances/{instanceId}/security/roles/assign",
                    JsonContent.Create(roleAssignmentRequest));

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<RoleAssignmentResult>(responseContent);

                    if (result == null)
                        return new RoleAssignmentResult() { Success = false };

                    return result;
                }

                _logger.LogError("The call to the Authorization API returned an error: {StatusCode} - {ReasonPhrase}.", response.StatusCode, response.ReasonPhrase);
                return new RoleAssignmentResult() { Success = false };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "There was an error calling the Authorization API");
                return new RoleAssignmentResult() { Success = false };
            }
        }

        /// <inheritdoc/>
        public async Task<ResourceProviderGetResult> ProcessGetRolesWithActions(string instanceId, GetRolesWithActionsRequest request)
        {
            try
            {
                var httpClient = await CreateHttpClient();
                var response = await httpClient.PostAsync(
                    $"/instances/{instanceId}/security/roles/actions",
                    JsonContent.Create(request));

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<ResourceProviderGetResult>(responseContent)!;
                }

                _logger.LogError("The call to the Authorization API returned an error: {StatusCode} - {ReasonPhrase}.", response.StatusCode, response.ReasonPhrase);
                return new ResourceProviderGetResult() { Roles = [], Actions = [] };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "There was an error calling the Authorization API");
                return new ResourceProviderGetResult() { Roles = [], Actions = [] };
            }
        }

        private async Task<HttpClient> CreateHttpClient()
        {
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.BaseAddress = new Uri(_settings.APIUrl);

            var credentials = DefaultAuthentication.AzureCredential;
            var tokenResult = await credentials.GetTokenAsync(
                new ([_settings.APIScope]),
                default);

            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", tokenResult.Token);

            return httpClient;
        }
    }
}
