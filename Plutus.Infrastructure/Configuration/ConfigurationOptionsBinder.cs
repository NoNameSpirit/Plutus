using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Plutus.Domain.Configuration;
using Plutus.Domain.Retry;

namespace Plutus.Infrastructure.Configuration;

/// <summary>
/// Turns the aggregated configuration into a PlutusProfile. It serializes the configuration tree to JSON and
/// deserializes it with the converters the profile loader uses.
/// </summary>
public sealed class ConfigurationOptionsBinder
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower),
            new TimeSpanConverter(),
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
                target[child.Key] = nestedChildren.ConvertAll(c => c.Value);
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

            target[child.Key] = bool.TryParse(child.Value, out var value) ? value : child.Value;
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
