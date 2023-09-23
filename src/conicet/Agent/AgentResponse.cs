using System.Text.Json.Serialization;
using MessagePack;

namespace MenosRelato.Agent;

/// <summary>
/// Response from the agent to a prompt.
/// </summary>
[MessagePackObject(true)]
public record AgentResponse(string Id, string Content, [property: JsonIgnore] AgentPrompt? Prompt = default)
{
    /// <summary>
    /// Gets or sets whether the response was cached.
    /// </summary>
    [IgnoreMember]
    public bool IsCached { get; init; }
}