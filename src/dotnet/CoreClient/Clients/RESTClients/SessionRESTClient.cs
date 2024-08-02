﻿using System.Text.Encodings.Web;
using System.Text.Json;
using Azure.Core;
using FoundationaLLM.Client.Core.Interfaces;
using FoundationaLLM.Common.Models.Chat;

namespace FoundationaLLM.Client.Core.Clients.RESTClients
{
    /// <summary>
    /// Provides methods to manage calls to the Core API's session endpoints.
    /// </summary>
    internal class SessionRESTClient(
        IHttpClientFactory httpClientFactory,
        TokenCredential credential,
        string instanceId) : CoreRESTClientBase(httpClientFactory, credential), ISessionRESTClient
    {
        private readonly string _instanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));

        /// <inheritdoc/>
        public async Task<string> CreateSessionAsync()
        {
            var coreClient = await GetCoreClientAsync();
            var responseSession = await coreClient.PostAsync($"instances/{_instanceId}/sessions", null);

            if (responseSession.IsSuccessStatusCode)
            {
                var responseContent = await responseSession.Content.ReadAsStringAsync();
                var sessionResponse = JsonSerializer.Deserialize<Session>(responseContent, SerializerOptions);
                if (sessionResponse?.SessionId != null)
                {
                    return sessionResponse.SessionId;
                }
            }

            throw new Exception($"Failed to create a new chat session. Status code: {responseSession.StatusCode}. Reason: {responseSession.ReasonPhrase}");
        }

        /// <inheritdoc/>
        public async Task<string> RenameChatSession(string sessionId, string sessionName)
        {
            var coreClient = await GetCoreClientAsync();
            var response = await coreClient.PostAsync($"instances/{_instanceId}/sessions/{sessionId}/rename?newChatSessionName={UrlEncoder.Default.Encode(sessionName)}", null);

            if (response.IsSuccessStatusCode)
            {
                return sessionName;
            }

            throw new Exception($"Failed to rename chat session. Status code: {response.StatusCode}. Reason: {response.ReasonPhrase}");
        }

        /// <inheritdoc/>
        public async Task<CompletionPrompt> GetCompletionPromptAsync(string sessionId, string completionPromptId)
        {
            var coreClient = await GetCoreClientAsync();
            var responseMessage = await coreClient.GetAsync($"instances/{_instanceId}/sessions/{sessionId}/completionprompts/{completionPromptId}");

            if (responseMessage.IsSuccessStatusCode)
            {
                var responseContent = await responseMessage.Content.ReadAsStringAsync();
                var completionPrompt =
                    JsonSerializer.Deserialize<CompletionPrompt>(responseContent, SerializerOptions);
                return completionPrompt ?? throw new InvalidOperationException("The returned completion prompt is invalid.");
            }

            throw new Exception($"Failed to get completion prompt. Status code: {responseMessage.StatusCode}. Reason: {responseMessage.ReasonPhrase}");
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<Message>> GetChatSessionMessagesAsync(string sessionId)
        {
            var coreClient = await GetCoreClientAsync();
            var responseMessage = await coreClient.GetAsync($"instances/{_instanceId}/sessions/{sessionId}/messages");

            if (responseMessage.IsSuccessStatusCode)
            {
                var responseContent = await responseMessage.Content.ReadAsStringAsync();
                var messages = JsonSerializer.Deserialize<IEnumerable<Message>>(responseContent, SerializerOptions);
                return messages ?? throw new InvalidOperationException("The returned messages are invalid.");
            }

            throw new Exception($"Failed to get chat session messages. Status code: {responseMessage.StatusCode}. Reason: {responseMessage.ReasonPhrase}");
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<Session>> GetAllChatSessionsAsync()
        {
            var coreClient = await GetCoreClientAsync();
            var responseMessage = await coreClient.GetAsync($"instances/{_instanceId}/sessions");

            if (responseMessage.IsSuccessStatusCode)
            {
                var responseContent = await responseMessage.Content.ReadAsStringAsync();
                var sessions = JsonSerializer.Deserialize<IEnumerable<Session>>(responseContent, SerializerOptions);
                return sessions ?? throw new InvalidOperationException("The returned sessions are invalid.");
            }

            throw new Exception($"Failed to retrieve chat sessions. Status code: {responseMessage.StatusCode}. Reason: {responseMessage.ReasonPhrase}");
        }

        /// <inheritdoc/>
        public async Task RateMessageAsync(string sessionId, string messageId, bool rating)
        {
            var coreClient = await GetCoreClientAsync();
            var responseMessage = await coreClient.PostAsync($"instances/{_instanceId}/sessions/{sessionId}/message/{messageId}/rate?rating={rating}", null);

            if (!responseMessage.IsSuccessStatusCode)
            {
                throw new Exception($"Failed to rate message. Status code: {responseMessage.StatusCode}. Reason: {responseMessage.ReasonPhrase}");
            }
        }

        /// <inheritdoc/>
        public async Task DeleteSessionAsync(string sessionId)
        {
            var coreClient = await GetCoreClientAsync();
            await coreClient.DeleteAsync($"instances/{_instanceId}/sessions/{sessionId}");
        }
    }
}
