using System.Text;
using Microsoft.Extensions.Configuration;
using Plutus.Domain;

namespace Plutus.Infrastructure.Configuration;

public sealed record PlutusConfigurationResult
{
    public IConfigurationRoot? Configuration { get; init; }
    public IReadOnlyList<IConfiguration> Layers { get; init; } = [];
    public string? Error { get; init; }
    public bool Succeeded => Error is null;
}

/// <summary>
/// Builds the aggregated configuration of a run: the implicit base profiles, the named ones in the order the user
/// stated them, and the PLUTUS_ environment variables. A later source overrides an earlier one.
/// </summary>
public sealed class PlutusConfigurationBuilder(FileProviderAccess files)
{
    public PlutusConfigurationResult Build(string workingDirectory, IReadOnlyList<string> namedProfiles)
    {
        var paths = new List<string>();

        var defaults = Path.Combine(workingDirectory, "default.plutusrc");
        if (files.FileExists(defaults))
        {
            paths.Add(defaults);
        }

        var basePath = Path.Combine(workingDirectory, ".plutusrc");
        if (files.FileExists(basePath))
        {
            paths.Add(basePath);
        }

        foreach (var profile in namedProfiles)
        {
            var path = Path.Combine(workingDirectory, $".{profile}.plutusrc");
            if (!files.FileExists(path))
            {
                return new PlutusConfigurationResult
                {
                    Error = $"profile '{profile}' does not exist, path: {path}."
                };
            }

            paths.Add(path);
        }

        var layerResult = GetRootsAndProviders(paths);

        return new PlutusConfigurationResult
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
        var configurationRoots = new List<IConfigurationRoot>();
        var mainBuilder = new ConfigurationBuilder();

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

        mainBuilder.AddEnvironmentVariables(prefix: "PLUTUS_");

        var environmentRoot = new ConfigurationBuilder()
            .AddEnvironmentVariables(prefix: "PLUTUS_")
            .Build();
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
