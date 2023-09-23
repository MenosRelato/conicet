namespace MenosRelato.Agent;

public interface IAgentService
{
    Task<AgentResponse> ProcessAsync(AgentPrompt prompt, CancellationToken cancellation = default);
}