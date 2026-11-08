using System.Text;
using Microsoft.Extensions.Configuration;
using Plutus.Domain;
using Plutus.Domain.Configuration;

namespace Plutus.Infrastructure.Configuration;

public sealed record ProfileConfigurationResult
{
    public IConfigurationRoot Configuration { get; init; } = new ConfigurationRoot([]);
    public IReadOnlyList<IConfiguration> Layers { get; init; } = [];
    public PlutusProfile? Profile { get; init; }
    public string? Error { get; init; }
    public string? Path { get; init; }
    public bool Succeeded => Error is null;
}

/// <summary>
/// Builds the aggregated configuration of a run: PLUTUS_ environment variables, implicit base profiles, and named
/// profiles in the order the user stated them. A later source overrides an earlier one.
/// </summary>
public sealed class PlutusConfigurationBuilder(FileProviderAccess files)
{
    public ProfileConfigurationResult Build(string workingDirectory, IReadOnlyList<string> namedProfiles, bool loadConfiguration)
    {
        if (!loadConfiguration)
        {
            return new ProfileConfigurationResult();
        }

        var locatedProfile = new PlutusProfileLoader(files).Locate(workingDirectory);
        var profileDirectory = locatedProfile is null ? workingDirectory : Path.GetDirectoryName(locatedProfile)!;

        var paths = new List<string>();

        var defaults = Path.Combine(profileDirectory, "default.plutusrc");
        if (files.FileExists(defaults))
        {
            paths.Add(defaults);
        }

        if (locatedProfile is not null)
        {
            paths.Add(locatedProfile);
        }

        foreach (var profile in namedProfiles)
        {
            var path = Path.Combine(profileDirectory, $"{profile}.plutusrc");
            if (!files.FileExists(path))
            {
                return new ProfileConfigurationResult
                {
                    Error = $"profile '{profile}' does not exist, path: {path}."
                };
            }

            paths.Add(path);
        }

        var layerResult = GetRootsAndProviders(paths);

        return new ProfileConfigurationResult
        {
            Configuration = new ConfigurationRoot(layerResult.Providers.ToList()),
            Layers = layerResult.Roots,
        };
    }

    /// <summary>
    /// Rebuilds each source on its own so the binder can union the arrays across layers instead of letting the last one win.
    /// </summary>
    private LayersResult GetRootsAndProviders(IReadOnlyList<string> paths)
    {
        var environmentRoot = new ConfigurationBuilder()
            .AddEnvironmentVariables(prefix: "PLUTUS_")
            .Build();

        var mainBuilder = new ConfigurationBuilder()
            .AddEnvironmentVariables(prefix: "PLUTUS_");

        var configurationRoots = new List<IConfigurationRoot>();
        foreach (var path in paths)
        {
            var json = files.ReadAllText(path);
            var bytes = Encoding.UTF8.GetBytes(json);

            var layerRoot = new ConfigurationBuilder()
                .AddJsonStream(new MemoryStream(bytes))
                .Build();
            configurationRoots.Add(layerRoot);

            mainBuilder.AddJsonStream(new MemoryStream(bytes));
        }

        configurationRoots.Add(environmentRoot);

        return new LayersResult
        {
            Roots = configurationRoots,
            Providers = mainBuilder.Sources.Select(s => s.Build(mainBuilder)).ToList()
        };
    }

    private sealed record LayersResult
    {
        public IReadOnlyList<IConfigurationRoot> Roots { get; init; } = [];
        public IReadOnlyList<IConfigurationProvider> Providers { get; init; } = [];
    }
}
