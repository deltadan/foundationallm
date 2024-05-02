﻿using FoundationaLLM.Core.Examples.Interfaces;
using FoundationaLLM.Core.Examples.Models;
using FoundationaLLM.Core.Examples.Setup;
using Xunit.Abstractions;

namespace FoundationaLLM.Core.Examples
{
    /// <summary>
    /// Example class for running the default FoundationaLLM agent completions in both session and sessionless modes.
    /// </summary>
    public class Example0001_FoundationaLLMAgentIntegration : BaseTest, IClassFixture<TestFixture>
	{
		private readonly IAgentConversationTestService _agentConversationTestService;

		public Example0001_FoundationaLLMAgentIntegration(ITestOutputHelper output, TestFixture fixture)
			: base(output, fixture.ServiceProvider)
		{
            _agentConversationTestService = GetService<IAgentConversationTestService>();
		}

		[Fact]
		public async Task RunAsync()
		{
			WriteLine("============ FoundationaLLM Agent Completions ============");
			await RunExampleAsync();
		}

		private async Task RunExampleAsync()
        {
            var userPrompt = "Who are you?";
            var agentName = Constants.Agents.FoundationaLLMAgentName;

            WriteLine($"Send session-based \"{userPrompt}\" user prompt to the {agentName} agent.");
            var response = await _agentConversationTestService.RunAgentCompletionWithSession(agentName, userPrompt);
            WriteLine($"Agent completion response: {response.Text}");
            Assert.False(string.IsNullOrWhiteSpace(response.Text) || response.Text == Constants.Agents.FailedCompletionResponse);
            WriteLine($"Send sessionless \"{userPrompt}\" user prompt to the {agentName} agent.");
            response = await _agentConversationTestService.RunAgentCompletionWithNoSession(agentName, userPrompt);
            WriteLine($"Agent completion response: {response.Text}");
            Assert.False(string.IsNullOrWhiteSpace(response.Text) || response.Text == Constants.Agents.FailedCompletionResponse);
        }
	}
}
