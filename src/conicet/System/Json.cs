using System.Text.Json;
using System.Text.Json.Serialization;

namespace MenosRelato;

public static class Json
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        AllowTrailingCommas = true,
    };
}
