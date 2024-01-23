﻿using FoundationaLLM.Management.Models.Configuration.Cache;

namespace FoundationaLLM.Management.Interfaces
{
    /// <summary>
    /// Contains base functionality for calling the AgentHubAPI service.
    /// </summary>
    public interface IAgentHubAPIService
    {
        /// <summary>
        /// Refreshes the configuration cache.
        /// </summary>
        /// <returns></returns>
        Task<APICacheRefreshResult> RefreshConfigurationCache();

        /// <summary>
        /// Refreshes the named cache.
        /// </summary>
        /// <param name="name">The name of the cache item to refresh.</param>
        /// <returns></returns>
        Task<APICacheRefreshResult> RefreshCache(string name);
    }
}
