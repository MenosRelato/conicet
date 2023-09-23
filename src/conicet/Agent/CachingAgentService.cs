using System.Numerics;
using System.Security.Cryptography;
using Azure;
using MessagePack;

namespace MenosRelato.Agent;

public class CachingAgentService(IAgentService agent) : IAgentService
{
    const string CacheDir = ".cache";
    static readonly MessagePackSerializerOptions options = MessagePackSerializerOptions.Standard.WithOmitAssemblyVersion(true);

    public async Task<AgentResponse> ProcessAsync(AgentPrompt prompt, CancellationToken cancellation)
    {
        var data = MessagePackSerializer.Serialize(prompt, options, cancellationToken: cancellation);
        var hash = Base62.Encode(BigInteger.Abs(new BigInteger(SHA256.HashData(data))));

        Directory.CreateDirectory(CacheDir);

        var answerFile = Path.Combine(CacheDir, $"{hash}-answer.bin");
        try
        {
            if (File.Exists(answerFile))
            {
                using var answer = File.OpenRead(answerFile);
                var cached = MessagePackSerializer.Deserialize<AgentResponse>(answer, options, cancellation);
                // The cached version will contain the original prompt Id.
                return cached with { Id = prompt.Id, Prompt = prompt with { Id = prompt.Id } };
            }
        }
        catch (MessagePackSerializationException)
        {
            // We failed, perhaps we changed the binary representation, we'll persist again.
        }
        catch (RequestFailedException)
        {
            // Blob didn't exist, we'll persist. Extra safety in case the blob is deleted concurrently.
        }

        var promptFile = Path.Combine(CacheDir, $"{hash}-prompt.bin");
        await File.WriteAllBytesAsync(promptFile, data, cancellation);

        var response = await agent.ProcessAsync(prompt, cancellation);
        var answerData = MessagePackSerializer.Serialize(response, options, cancellation);
        await File.WriteAllBytesAsync(answerFile, answerData, cancellation);

        return response;
    }
}
