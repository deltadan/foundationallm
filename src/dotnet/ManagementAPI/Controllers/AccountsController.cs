﻿using FoundationaLLM.Common.Interfaces;
using FoundationaLLM.Common.Models.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FoundationaLLM.Management.API.Controllers
{
    /// <summary>
    /// Provides account retrieval methods.
    /// </summary>
    /// <param name="callContext">The call context containing user identity details.</param>
    /// <param name="accountService">The <see cref="IAccountService"/> used for retrieving group account information.</param>
    /// <param name="logger">The <see cref="ILogger"/> used for logging.</param>
    [Authorize(Policy = "DefaultPolicy")]
    [ApiController]
    [Consumes("application/json")]
    [Produces("application/json")]
    [Route($"instances/{{instanceId}}/accounts")]
    public class AccountsController(
        ICallContext callContext,
        IAccountService accountService,
        ILogger<AccountsController> logger) : Controller
    {
        private readonly ILogger<AccountsController> _logger = logger;
        private readonly ICallContext _callContext = callContext;

        /// <summary>
        /// Retrieves a list of group accounts with filtering and paging options.
        /// </summary>
        /// <returns></returns>
        [HttpGet("groups", Name = "GetGroups")]
        public async Task<IActionResult> GetGroups(AccountQueryParameters parameters)
        {
            var groups = await accountService.GetUserGroupsAsync(parameters);
            return new OkObjectResult(groups);
        }

        /// <summary>
        /// Retrieves a specific group account by its identifier.
        /// </summary>
        /// <param name="groupId"></param>
        /// <returns></returns>
        [HttpGet("groups/{groupId}", Name = "GetGroup")]
        public async Task<IActionResult> GetGroup(string groupId)
        {
            var group = await accountService.GetUserGroupByIdAsync(groupId);
            return new OkObjectResult(group);
        }

        /// <summary>
        /// Retrieves a list of user accounts with filtering and paging options.
        /// </summary>
        /// <returns></returns>
        [HttpGet("users", Name = "GetUsers")]
        public async Task<IActionResult> GetUsers(AccountQueryParameters parameters)
        {
            var users = await accountService.GetUsersAsync(parameters);
            return new OkObjectResult(users);
        }

        /// <summary>
        /// Retrieves a specific user account by its identifier.
        /// </summary>
        /// <param name="userId"></param>
        /// <returns></returns>
        [HttpGet("users/{userId}", Name = "GetUser")]
        public async Task<IActionResult> GetUser(string userId)
        {
            var user = await accountService.GetUserByIdAsync(userId);
            return new OkObjectResult(user);
        }
    }
}
