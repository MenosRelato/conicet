using Azure.AI.OpenAI;

namespace MenosRelato.Agent;

static class AgentToChatExtensions
{
    public static ChatCompletionsOptions ToChat(this AgentPrompt prompt)
    {
        var options = new ChatCompletionsOptions();

        foreach (var message in prompt.Messages)
            options.Messages.Add(new ChatMessage(Convert(message.Role), message.Content));

        return options;
    }

    static ChatRole Convert(AgentMessageRole role) => role switch
    {
        AgentMessageRole.Assistant => ChatRole.Assistant,
        AgentMessageRole.System => ChatRole.System,
        _ => ChatRole.User,
    };
}
