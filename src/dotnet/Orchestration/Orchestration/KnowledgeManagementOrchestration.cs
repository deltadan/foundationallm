﻿using FoundationaLLM.Common.Constants.Agents;
using FoundationaLLM.Common.Constants.ResourceProviders;
using FoundationaLLM.Common.Exceptions;
using FoundationaLLM.Common.Extensions;
using FoundationaLLM.Common.Interfaces;
using FoundationaLLM.Common.Models.Orchestration.Request;
using FoundationaLLM.Common.Models.Orchestration.Response;
using FoundationaLLM.Common.Models.Orchestration.Response.OpenAI;
using FoundationaLLM.Common.Models.ResourceProviders.Agent;
using FoundationaLLM.Common.Models.ResourceProviders.Attachment;
using FoundationaLLM.Common.Models.ResourceProviders.AzureOpenAI;
using FoundationaLLM.Orchestration.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace FoundationaLLM.Orchestration.Core.Orchestration
{
    /// <summary>
    /// Knowledge Management orchestration.
    /// </summary>
    /// <remarks>
    /// Constructor for default agent.
    /// </remarks>
    /// <param name="instanceId">The FoundationaLLM instance ID.</param>
    /// <param name="agent">The <see cref="KnowledgeManagementAgent"/> agent.</param>
    /// <param name="explodedObjects">A dictionary of objects retrieved from various object ids related to the agent. For more details see <see cref="LLMCompletionRequest.Objects"/> .</param>
    /// <param name="callContext">The call context of the request being handled.</param>
    /// <param name="orchestrationService"></param>
    /// <param name="logger">The logger used for logging.</param>
    /// <param name="resourceProviderServices">The dictionary of <see cref="IResourceProviderService"/></param>
    /// <param name="dataSourceAccessDenied">Inidicates that access was denied to all underlying data sources.</param>
    public class KnowledgeManagementOrchestration(
        string instanceId,
        KnowledgeManagementAgent agent,
        Dictionary<string, object> explodedObjects,
        ICallContext callContext,
        ILLMOrchestrationService orchestrationService,
        ILogger<OrchestrationBase> logger,
        Dictionary<string, IResourceProviderService> resourceProviderServices,
        bool dataSourceAccessDenied) : OrchestrationBase(orchestrationService)
    {
        private readonly string _instanceId = instanceId;
        private readonly KnowledgeManagementAgent _agent = agent;
        private readonly Dictionary<string, object> _explodedObjects = explodedObjects;
        private readonly ICallContext _callContext = callContext;
        private readonly ILogger<OrchestrationBase> _logger = logger;
        private readonly bool _dataSourceAccessDenied = dataSourceAccessDenied;
        private readonly string _fileUserContextName = $"{callContext.CurrentUserIdentity!.UPN?.NormalizeUserPrincipalName() ?? callContext.CurrentUserIdentity!.UserId}-file-{instanceId.ToLower()}";
        private readonly string _fileUserContextObjectId = $"/instances/{instanceId}/providers/{ResourceProviderNames.FoundationaLLM_AzureOpenAI}/{AzureOpenAIResourceTypeNames.FileUserContexts}/"
            + $"{callContext.CurrentUserIdentity!.UPN?.NormalizeUserPrincipalName() ?? callContext.CurrentUserIdentity!.UserId}-file-{instanceId.ToLower()}";

        private readonly IResourceProviderService _attachmentResourceProvider =
            resourceProviderServices[ResourceProviderNames.FoundationaLLM_Attachment];
        private readonly IResourceProviderService _azureOpenAIResourceProvider =
            resourceProviderServices[ResourceProviderNames.FoundationaLLM_AzureOpenAI];

        /// <inheritdoc/>
        public override async Task<CompletionResponse> GetCompletion(CompletionRequest completionRequest)
        {
            if (_dataSourceAccessDenied)
                return new CompletionResponse
                {
                    OperationId = completionRequest.OperationId!,
                    Completion = "I have no knowledge that can be used to answer this question.",
                    UserPrompt = completionRequest.UserPrompt!,
                    AgentName = _agent.Name
                };

            if (_agent.ExpirationDate.HasValue && _agent.ExpirationDate.Value < DateTime.UtcNow)
                return new CompletionResponse
                {
                    OperationId = completionRequest.OperationId!,
                    Completion = $"The requested agent, {_agent.Name}, has expired and is unable to respond.",
                    UserPrompt = completionRequest.UserPrompt!,
                    AgentName = _agent.Name
                };

            var result = await _orchestrationService.GetCompletion(
                instanceId,
                new LLMCompletionRequest
                {
                    OperationId = completionRequest.OperationId,
                    UserPrompt = completionRequest.UserPrompt!,
                    MessageHistory = completionRequest.MessageHistory,
                    Attachments = completionRequest.Attachments == null ? [] : await GetAttachmentPaths(completionRequest.Attachments),
                    Agent = _agent,
                    Objects = _explodedObjects
                });

            if (result.Citations != null)
            {
                result.Citations = result.Citations
                    .GroupBy(c => c.Filepath)
                    .Select(g => g.First())
                    .ToArray();
            }

            return new CompletionResponse
            {
                OperationId = completionRequest.OperationId!,
                Completion = result.Completion!,
                Content = await TransformContentItems(result.Content!),
                UserPrompt = completionRequest.UserPrompt!,
                Citations = result.Citations,
                FullPrompt = result.FullPrompt,
                PromptTemplate = result.PromptTemplate,
                AgentName = result.AgentName,
                PromptTokens = result.PromptTokens,
                CompletionTokens = result.CompletionTokens,
                AnalysisResults = result.AnalysisResults
            };
        }

        private async Task<List<AttachmentProperties>> GetAttachmentPaths(List<string> attachmentObjectIds)
        {
            if (attachmentObjectIds.Count == 0)
                return [];

            var attachments = attachmentObjectIds
                .ToAsyncEnumerable()
                .SelectAwait(async x => await _attachmentResourceProvider.GetResource<AttachmentFile>(x, _callContext.CurrentUserIdentity!));

            var fileUserContext = await _azureOpenAIResourceProvider.GetResource<FileUserContext>(
                _fileUserContextObjectId,
                _callContext.CurrentUserIdentity!);

            List<AttachmentProperties> result = [];
            await foreach (var attachment in attachments)
            {
                var useAttachmentPath =
                    string.IsNullOrWhiteSpace(attachment.SecondaryProvider)
                    || (attachment.ContentType ?? string.Empty).StartsWith("image/", StringComparison.OrdinalIgnoreCase);

                result.Add(new AttachmentProperties
                {
                    OriginalFileName = attachment.OriginalFileName,
                    ContentType = attachment.ContentType!,
                    Provider = useAttachmentPath
                        ? ResourceProviderNames.FoundationaLLM_Attachment
                        : attachment.SecondaryProvider!,
                    ProviderFileName = useAttachmentPath
                        ? attachment.Path
                        : fileUserContext.Files[attachment.ObjectId!].OpenAIFileId!,
                    ProviderStorageAccountName = useAttachmentPath
                        ? _attachmentResourceProvider.StorageAccountName
                        : null
                });
            }

            return result;
        }

        private async Task<List<MessageContentItemBase>> TransformContentItems(List<MessageContentItemBase> contentItems)
        {
            List<FileMapping> newFileMappings = [];
            var result = contentItems.Select(ci => TransformContentItem(ci, newFileMappings)).ToList();

            var fileUserContext = await _azureOpenAIResourceProvider.GetResource<FileUserContext>(
                _fileUserContextObjectId,
                _callContext.CurrentUserIdentity!);

            foreach (var fileMapping in newFileMappings)
                fileUserContext.Files.Add(
                    fileMapping.FoundationaLLMObjectId,
                    fileMapping);

            await _azureOpenAIResourceProvider.UpsertResourceAsync<FileUserContext, FileUserContextUpsertResult>(
                _fileUserContextObjectId,
                fileUserContext,
                _callContext.CurrentUserIdentity!);

            return result;
        }

        private MessageContentItemBase TransformContentItem(MessageContentItemBase contentItem, List<FileMapping> newFileMappings) =>
            contentItem.AgentCapabilityCategory switch
            {
                AgentCapabilityCategoryNames.OpenAIAssistants => TransformOpenAIAssistantsContentItem(contentItem, newFileMappings),
                AgentCapabilityCategoryNames.FoundationaLLMKnowledgeManagement => TransformFoundationaLLMKnowledgeManagementContentItem(contentItem),
                _ => throw new OrchestrationException($"The agent capability category {contentItem.AgentCapabilityCategory} is not supported.")
            };

        #region OpenAI Assistants content items

        private MessageContentItemBase TransformOpenAIAssistantsContentItem(MessageContentItemBase contentItem, List<FileMapping> newFileMappings) =>
            contentItem switch
            {
                OpenAIImageFileMessageContentItem openAIImageFile => TransformOpenAIAssistantsImageFile(openAIImageFile, newFileMappings),
                OpenAITextMessageContentItem openAITextMessage => TransformOpenAIAssistantsTextMessage(openAITextMessage, newFileMappings),
                _ => throw new OrchestrationException($"The content item type {contentItem.GetType().Name} is not supported.")
            };

        private OpenAIImageFileMessageContentItem TransformOpenAIAssistantsImageFile(OpenAIImageFileMessageContentItem openAIImageFile, List<FileMapping> newFileMappings)
        {
            newFileMappings.Add(new FileMapping
            {
                FoundationaLLMObjectId = $"/instances/{_instanceId}/providers/{ResourceProviderNames.FoundationaLLM_AzureOpenAI}/{AzureOpenAIResourceTypeNames.FileUserContexts}/{_fileUserContextName}/{AzureOpenAIResourceTypeNames.FilesContent}/{openAIImageFile.FileId}",
                OriginalFileName = openAIImageFile.FileId!,
                ContentType = "image/png",
                OpenAIFileId = openAIImageFile.FileId!,
                Generated = true,
                OpenAIFileGeneratedOn = DateTimeOffset.UtcNow
            });
            openAIImageFile.FileUrl = $"{{{{fllm_base_url}}}}/instances/{_instanceId}/files/{ResourceProviderNames.FoundationaLLM_AzureOpenAI}/{openAIImageFile.FileId}";
            return openAIImageFile;
        }

        private OpenAIFilePathContentItem TransformOpenAIAssistantsFilePath(OpenAIFilePathContentItem openAIFilePath, List<FileMapping> newFileMappings)
        {
            newFileMappings.Add(new FileMapping
            {
                FoundationaLLMObjectId = $"/instances/{_instanceId}/providers/{ResourceProviderNames.FoundationaLLM_AzureOpenAI}/{AzureOpenAIResourceTypeNames.FileUserContexts}/{_fileUserContextName}/{AzureOpenAIResourceTypeNames.FilesContent}/{openAIFilePath.FileId}",
                OriginalFileName = openAIFilePath.FileId!,
                ContentType = "application/octet-stream",
                OpenAIFileId = openAIFilePath.FileId!,
                Generated = true,
                OpenAIFileGeneratedOn = DateTimeOffset.UtcNow
            });
            openAIFilePath.FileUrl = $"{{{{fllm_base_url}}}}/instances/{_instanceId}/files/{ResourceProviderNames.FoundationaLLM_AzureOpenAI}/{openAIFilePath.FileId}";
            return openAIFilePath;
        }

        private OpenAITextMessageContentItem TransformOpenAIAssistantsTextMessage(OpenAITextMessageContentItem openAITextMessage, List<FileMapping> newFileMappings)
        {
            openAITextMessage.Annotations = openAITextMessage.Annotations
                .Select(a => TransformOpenAIAssistantsFilePath(a, newFileMappings))
                .ToList();
            var sandboxPlaceholders = openAITextMessage.Annotations.ToDictionary(
                a => a.Text!,
                a => $"{{{{fllm_base_url}}}}{a.FileUrl!}");

            var input = openAITextMessage.Value!;
            var regex = new Regex(@"\(sandbox:[^)]*\)");
            var matches = regex.Matches(input);

            if (matches.Count == 0)
                return openAITextMessage;

            Match? previousMatch = null;
            List<string> output = [];

            foreach (Match match in matches)
            {
                var startIndex = previousMatch == null ? 0 : previousMatch.Index + previousMatch.Length;
                output.Add(input.Substring(startIndex, match.Index - startIndex));
                var token = input.Substring(match.Index, match.Length);
                if (sandboxPlaceholders.TryGetValue(token, out var replacement))
                    output.Add(replacement);
                else
                    output.Add(token);

                previousMatch = match;
            }

            output.Add(input.Substring(previousMatch!.Index + previousMatch.Length));

            openAITextMessage.Value = string.Join("", output);
            return openAITextMessage;
        }

        #endregion

        #region FoundationaLLM Knowledge Management content items

        private MessageContentItemBase TransformFoundationaLLMKnowledgeManagementContentItem(MessageContentItemBase contentItem) =>
            contentItem;

        #endregion
    }
}
