using System.Text.Json;
using System.Text.Json.Serialization;

namespace MenosRelato.Commands.Cache;

[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Item))]
[JsonSerializable(typeof(Meta))]
[JsonSerializable(typeof(Area))]
[JsonSerializable(typeof(Author))]
[JsonSerializable(typeof(List<Author>))]
public partial class ScrapGenerationContext : JsonSerializerContext
{
    public static JsonSerializerOptions JsonOptions { get; } = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = ScrapGenerationContext.Default,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

public record Area(int Id, string Name)
{
    public static Area Create(string name) => new(name.ToSubject(), name);
}

public record Author(string Id, string Name);
public record Meta(string Name, string Content, string? Lang);
public record Item(Area Area, string Title, string Handle, DateOnly Date, List<Meta> Metadata)
{
    public List<Author> Authors { get; init; } = new();
    public List<Author> Collaborators { get; init; } = new();
}