using Azure.AI.OpenAI;
using Azure;
using Microsoft.Extensions.Configuration;

namespace MenosRelato.Agent;

/// <summary>
/// OpenAI agent service implementation.
/// </summary>
public class CloudAgentService(IConfiguration configuration) : IAgentService
{
    // 0.5 recommended for chatbot
    readonly float defaultTemperature = float.TryParse(configuration?["OpenAI:Temperature"], out var temp) ? temp : 0.5f;

    public async Task<AgentResponse> ProcessAsync(AgentPrompt prompt, CancellationToken cancellation)
    {
        var useAzure = prompt.Model != "gpt-4";
        //var userId = user.ǃ(Claims.Id);

        // TODO: if user is from Argentina, use AR-financed model instead

        // Use Azure for 3.5 turbo, and OpenAI for the rest
        var client = useAzure
            ? new OpenAIClient(
                new Uri("https://chebot.openai.azure.com/"),
                new AzureKeyCredential(configuration.ǃ("Azure:OpenAI")))
            : new OpenAIClient(configuration.ǃ("OpenAI:Key"));

        var deployment = useAzure ? prompt.Model.Replace(".", "") : prompt.Model;
        var request = prompt.ToChat();

        request.Temperature = prompt.Temperature ?? defaultTemperature;
        request.MaxTokens = prompt.MaxTokens;

        if (prompt.Temperature == null)
            prompt = prompt with { Temperature = defaultTemperature };

        var completions = await client.GetChatCompletionsAsync(deployment, request, cancellation);

        if (!completions.HasValue)
            throw new InvalidOperationException("Did not get a response from the model.");

        var message = completions.Value.Choices[0].Message;
        var response = new AgentResponse(prompt.Id, message.Content, prompt);

        return response;
    }
}