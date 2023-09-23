using System.Text.Json.Serialization;
using MessagePack;

namespace MenosRelato.Agent;

/// <summary>
/// Represents a message in an interaction with the bot.
/// </summary>
/// <param name="Content">The message content.</param>
/// <param name="Role">The role played by the message.</param>
[MessagePackObject(true)]
public record AgentMessage(AgentMessageRole Role, string Content)
{
    [Obsolete("Serialization-only constructor", true)]
    [JsonConstructor]
    public AgentMessage() : this(AgentMessageRole.User, "") { }

    /// <summary>
    /// Creates a new message with the <see cref="AgentMessageRole.User"/> role.
    /// </summary>
    public AgentMessage(string content) : this(AgentMessageRole.User, content) { }
}