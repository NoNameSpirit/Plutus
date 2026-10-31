using System.Text.Json;
using System.Text.Json.Serialization;
using Plutus.Domain.Retry;

namespace Plutus.Domain.Configuration;

/// <summary>The outcome of reading a profile: the profile itself, or the reason it could not be used.</summary>
public sealed record ProfileLoadResult
{
    public PlutusProfile? Profile { get; init; }

    /// <summary>The file that was read, or the one that was looked for and not found.</summary>
    public string? Path { get; init; }

    /// <summary>Non-null when a profile exists but cannot be trusted. A run must not continue past this.</summary>
    public string? Error { get; init; }

    public bool Succeeded => Error is null;

    public bool Found => Profile is not null;
}

/// <summary>
/// Finds and reads the <c>.plutusrc</c> profile of a run. The file is JSON (comments and trailing commas are
/// accepted, so it reads like a configuration file rather than a payload), every key is validated, and an unknown key
/// is an error: a typo in a profile must not look like "this repository wants the defaults".
/// <para>
/// The search and the read go through <see cref="FileProviderAccess"/>, so nothing here touches a disk directly and a
/// test describes a tree instead of creating one.
/// </para>
/// </summary>
public sealed class PlutusProfileLoader(FileProviderAccess files)
{
    /// <summary>Environment variable naming a profile explicitly, for a run outside any repository.</summary>
    public const string PathVariable = "PLUTUS_PROFILE";

    /// <summary>The profile names, in the order a directory is searched.</summary>
    public static readonly IReadOnlyList<string> FileNames = [".plutusrc", ".plutusrc.json"];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower),
            new TimeSpanConverter(),
        },
    };

    /// <summary>
    /// The profile that governs <paramref name="startDirectory"/>: the nearest one in that directory or any parent,
    /// stopping at the git repository root so a profile kept above the repository cannot be picked up by accident.
    /// </summary>
    public string? Locate(string startDirectory)
    {
        var directory = Path.GetFullPath(startDirectory);
        while (true)
        {
            foreach (var fileName in FileNames)
            {
                var candidate = Path.Combine(directory, fileName);
                if (files.FileExists(candidate))
                {
                    return candidate;
                }
            }

            // The repository root is the last directory that may hold the profile of this project.
            if (files.DirectoryExists(Path.Combine(directory, ".git")))
            {
                return null;
            }

            var parent = Path.GetDirectoryName(directory);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, directory, StringComparison.Ordinal))
            {
                return null;
            }

            directory = parent;
        }
    }

    /// <summary>Reads one profile file and validates it.</summary>
    public ProfileLoadResult Load(string path)
    {
        string json;
        try
        {
            json = files.ReadAllText(path);
        }
        catch (Exception exception)
        {
            return Failure(path, $"cannot read profile '{path}': {exception.Message}");
        }

        PlutusProfile? profile;
        try
        {
            profile = JsonSerializer.Deserialize<PlutusProfile>(json, SerializerOptions);
        }
        catch (JsonException exception)
        {
            var unknown = FindUnknownKeys(json);
            return unknown.Count > 0
                ? Failure(path, $"profile '{path}' has unknown key(s): {string.Join(", ", unknown.Select(key => $"'{key}'"))}. "
                                + $"Known keys are: {string.Join(", ", KnownKeys().Order(StringComparer.Ordinal))}.")
                : Failure(path, $"profile '{path}' is not a valid profile: {exception.Message}");
        }

        if (profile is null)
        {
            return Failure(path, $"profile '{path}' is empty; an empty profile changes nothing, so remove the file or state what it should set");
        }

        return Validate(profile, path) is { } error
            ? Failure(path, error)
            : new ProfileLoadResult { Profile = profile, Path = path };
    }

    private static ProfileLoadResult Failure(string path, string error) => new() { Path = path, Error = error };

    /// <summary>The key names a profile accepts, in the spelling the file uses.</summary>
    private static HashSet<string> KnownKeys() =>
        typeof(PlutusProfile)
            .GetProperties()
            .Where(property => property.CanWrite)
            .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The keys a profile states that it does not accept. They are found before deserializing, so the message can
    /// name them and list what would have worked instead of quoting the type name of a CLR record.
    /// </summary>
    private static List<string> FindUnknownKeys(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var known = KnownKeys();
            return document.RootElement
                .EnumerateObject()
                .Select(property => property.Name)
                .Where(key => !known.Contains(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            // Malformed JSON is reported by the deserializer, with the position it failed at.
            return [];
        }
    }

    private static string? Validate(PlutusProfile profile, string path)
    {
        if (profile.MaxTokens is <= 0)
        {
            return $"profile '{path}': 'maxTokens' must be greater than zero";
        }

        if (profile.MaxConcurrency is <= 0)
        {
            return $"profile '{path}': 'maxConcurrency' must be at least 1";
        }

        if (profile.MaxFileSize is { } size)
        {
            try
            {
                _ = FileSize.Parse(size);
            }
            catch (Exception exception)
            {
                return $"profile '{path}': 'maxFileSize' {exception.Message}";
            }
        }

        if (profile.IncludeExtensions is { } extensions)
        {
            foreach (var extension in extensions)
            {
                if (string.IsNullOrWhiteSpace(extension))
                {
                    return $"profile '{path}': 'includeExtensions' contains an empty entry";
                }

                if (extension.Contains('/') || extension.Contains('\\'))
                {
                    return $"profile '{path}': 'includeExtensions' entry '{extension}' is a path; use 'includeFiles' for patterns that name directories";
                }
            }
        }

        // 'format' and 'encoding' are validated where they are used: the bundler rejects a format it has no writer
        // for and the CLI rejects an encoding the platform does not know, both with the list of accepted values.
        return null;
    }
}
