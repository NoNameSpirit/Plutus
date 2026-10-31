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

        var builder = new ConfigurationBuilder();
        foreach (var path in paths)
        {
            var json = files.ReadAllText(path);
            builder.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)));
        }

        builder.AddEnvironmentVariables(prefix: "PLUTUS_");

        var configuration = builder.Build();
        var layers = BuildLayers(paths);

        return new PlutusConfigurationResult
        {
            Configuration = configuration,
            Layers = layers,
        };
    }

    /// <summary>
    /// Rebuilds each source on its own so the binder can union the arrays across layers instead of letting the last one win.
    /// </summary>
    private IReadOnlyList<IConfiguration> BuildLayers(IReadOnlyList<string> paths)
    {
        var result = new List<IConfiguration>();
        foreach (var path in paths)
        {
            var json = files.ReadAllText(path);
            result.Add(new ConfigurationBuilder()
                .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
                .Build());
        }

        result.Add(new ConfigurationBuilder()
            .AddEnvironmentVariables(prefix: "PLUTUS_")
            .Build());

        return result;
    }
}
