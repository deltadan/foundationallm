﻿using FoundationaLLM.Common.Models.Orchestration;

namespace FoundationaLLM.Gatekeeper.Core.Interfaces;

public interface IAgentFactoryAPIService
{
    Task<CompletionResponseBase> GetCompletion(CompletionRequestBase completionRequest);
    Task<string> GetSummary(string content);
}