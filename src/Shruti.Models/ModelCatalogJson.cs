using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shruti.Models;

public static class ModelCatalogJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(ModelCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return JsonSerializer.Serialize(catalog, Options);
    }

    public static ModelCatalog Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        return JsonSerializer.Deserialize<ModelCatalog>(json, Options)
            ?? throw new InvalidOperationException("The model catalog JSON did not contain a catalog.");
    }
}
