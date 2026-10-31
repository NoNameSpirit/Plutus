using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Plutus.Domain;
using Plutus.Domain.Diagnostics;
using Plutus.Infrastructure;
using Plutus.Infrastructure.Diagnostics;

namespace Plutus.CLI;

/// <summary>
/// Runs one bundle: builds the container for the resolved options, runs it, and prints the report.
/// The command object decides what a run means; this decides how it is hosted, so both stay small
/// and neither knows the other's details.
/// </summary>
internal static class BundleRunHost
{
    public static async Task<int> RunAsync(RunAppOptions options, IRunOutput output, FileProviderAccess files, IConfiguration configuration)
    {
        Console.OutputEncoding = options.TargetEncoding;
        Console.InputEncoding = options.TargetEncoding;

        using var cts = new CancellationTokenSource();

        // Use a volatile flag to safely coordinate across threads without heavy locks
        bool cancellationRequested = false;

        // Wire Ctrl+C to cancellation
        ConsoleCancelEventHandler consoleOnCancelKeyPress = (s, e) =>
        {
            // If Ctrl+C was already pressed, let the OS terminate the app on the second press
            if (Volatile.Read(ref cancellationRequested))
            {
                output.Warning("Forcing termination...");
                e.Cancel = false; // This tells the OS to kill the process immediately
                return;
            }

            Volatile.Write(ref cancellationRequested, true);
            e.Cancel = true; // Prevent immediate process termination for graceful cleanup
            output.Warning("Cancellation requested (Ctrl+C). Cleaning up...");

            try
            {
                // Safely trigger cancellation only if the source hasn't been disposed yet
                // ReSharper disable once AccessToDisposedClosure
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Safe guard against race conditions during host shutdown
            }
        };

        Console.CancelKeyPress += consoleOnCancelKeyPress;

        try
        {
            var services = new ServiceCollection();
            services.AddSingleton(configuration);
            services.AddInfrastructureServices(options, output, files);
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true,
            });
            await using var scope = provider.CreateAsyncScope();

            var exitCode = await scope.ServiceProvider.GetRequiredService<BundleAppRunner>().RunAsync(options, cts.Token);

            scope.ServiceProvider.GetRequiredService<IRunReportWriter>().Write();

            return exitCode;
        }
        finally
        {
            // Unsubscribe cleanly to prevent memory leaks from the static Console event
            Console.CancelKeyPress -= consoleOnCancelKeyPress;
        }
    }
}
