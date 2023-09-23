using System.Text.Json.Serialization;
using MessagePack;

namespace MenosRelato.Agent;

/// <summary>
/// Prompt for the agent to generate a response.
/// </summary>
[MessagePackObject(true)]
public record AgentPrompt
{
    /// <summary>
    /// Creates a prompt with the specified user message.
    /// </summary>
    /// <param name="message"></param>
    public AgentPrompt(string message, string model = "gpt-4") : this(new AgentMessage(message)) 
        => Model = model;

    [Obsolete("Serialization-only constructor", true)]
    [JsonConstructor]
    public AgentPrompt() : this([]) { }

    public AgentPrompt(params AgentMessage[] messages) => Messages = new(messages);

    /// <summary>
    /// Whether to force a refresh of the prompt, even if it's cached by the 
    /// <see cref="Caching"/> setting.
    /// </summary>
    [IgnoreMember]
    public bool ForceRefresh { get; init; } = false;

    /// <summary>
    /// An identifier for the interaction, for reporting and tracking purposes.
    /// </summary>
    [IgnoreMember]
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Optional model to use for the prompt. 
    /// </summary>
    public string Model { get; init; } = "gpt-4";

    /// <summary>
    /// Gets or sets the sampling temperature to use that controls the apparent creativity
    /// of generated completions. Has a valid range of 0.0 to 2.0.
    /// </summary>
    /// <remarks>
    /// Higher values will make output more random while lower values will make results
    /// more focused and deterministic.
    /// </remarks>
    public float? Temperature { get; init; }

    /// <summary>
    /// Gets or sets the maximum number of tokens to generate.
    /// </summary>
    public int? MaxTokens { get; init; }

    /// <summary>
    /// Messages to submit for the agent to process and generate a response.
    /// </summary>
    public List<AgentMessage> Messages { get; init; }
}