using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Plutus.Domain.Configuration;
using Plutus.Domain.Retry;

namespace Plutus.Infrastructure.Configuration;

/// <summary>
/// Turns the aggregated configuration into a PlutusProfile. It serializes the configuration tree to JSON and
/// deserializes it with the converters the profile loader uses.
/// (Built-in binder cannot read a kebab-case enum, a duration such as "2m", or a boolean stored as "True").
/// </summary>
public sealed class ConfigurationOptionsBinder
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower),
            new TimeSpanConverter(),
            new StringToBoolConverter(),
            new StringToIntConverter(),
        },
    };

    public PlutusProfile Bind(IConfiguration configuration, IReadOnlyList<IConfiguration> layers)
    {
        var json = JsonSerializer.Serialize(ToDictionary(configuration));
        var profile = JsonSerializer.Deserialize<PlutusProfile>(json, SerializerOptions) ?? new PlutusProfile();

        return profile with
        {
            IncludeFiles = MergeArray("includeFiles", layers) ?? profile.IncludeFiles,
            IncludeDirs = MergeArray("includeDirs", layers) ?? profile.IncludeDirs,
            IncludeExtensions = MergeArray("includeExtensions", layers) ?? profile.IncludeExtensions,
            Exclude = MergeArray("exclude", layers) ?? profile.Exclude,
            ExcludeDirs = MergeArray("excludeDirs", layers) ?? profile.ExcludeDirs,
            ExcludeFiles = MergeArray("excludeFiles", layers) ?? profile.ExcludeFiles,
        };
    }

    private static Dictionary<string, object?> ToDictionary(IConfiguration configuration)
    {
        var root = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        Fill(configuration.GetChildren(), root);

        return root;
    }

    private static void Fill(IEnumerable<IConfigurationSection> children, Dictionary<string, object?> target)
    {
        foreach (var child in children)
        {
            var nestedChildren = child.GetChildren().ToList();
            if (nestedChildren.Count > 0 && nestedChildren.All(c => int.TryParse(c.Key, out _)))
            {
                target[child.Key] = nestedChildren.Select(c => c.Value).ToList();
                continue;
            }

            if (nestedChildren.Count > 0)
            {
                var nested = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                Fill(nestedChildren, nested);
                target[child.Key] = nested;
                continue;
            }

            if (string.IsNullOrEmpty(child.Value))
            {
                continue;
            }

            target[child.Key] = child.Value;
        }
    }

    private static IReadOnlyList<string>? MergeArray(string key, IReadOnlyList<IConfiguration> layers)
    {
        HashSet<string>? result = null;
        foreach (var layer in layers)
        {
            var section = layer.GetSection(key);
            if (!section.Exists())
            {
                continue;
            }

            result ??= new HashSet<string>(StringComparer.Ordinal);
            foreach (var child in section.GetChildren())
            {
                if (child.Value is null)
                {
                    continue;
                }

                result.Add(child.Value);
            }
        }

        return result?.ToList();
    }
}

internal sealed class StringToBoolConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.True)
        {
            return true;
        }

        if (reader.TokenType == JsonTokenType.False)
        {
            return false;
        }

        if (reader.TokenType == JsonTokenType.String && bool.TryParse(reader.GetString(), out var value))
        {
            return value;
        }

        return default;
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) =>
        writer.WriteBooleanValue(value);
}

internal sealed class StringToIntConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetInt32();
        }

        if (reader.TokenType == JsonTokenType.String && int.TryParse(reader.GetString(), out var value))
        {
            return value;
        }

        return default;
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value);
}
