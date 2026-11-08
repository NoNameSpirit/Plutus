using System.CommandLine;
using Plutus.Domain;
using Plutus.Domain.ArchiveExtractor;
using Plutus.Domain.Comments;
using Plutus.Domain.Configuration;
using Plutus.Domain.Diagnostics;
using Plutus.Domain.Retry;
using Plutus.Infrastructure;
using Plutus.Infrastructure.Configuration;

namespace Plutus.CLI;

/// <summary>
/// The <c>bundle</c> command: collect the given project paths into one document. It is a command object like
/// <c>init</c>, so the tree has one entry per job and a new job is a new command, and its action is a short sequence
/// of named steps — read what was stated, resolve the profile, resolve the comment strategy, run.
/// </summary>
internal static class BundleCommand
{
    public static Command Create(IRunOutput output)
    {
        var options = new BundleOptions().WithAliases();
        var command = new Command("bundle", "Collect the given project paths into a single document.");
        command.Add(options.ProjectPath);
        foreach (var option in options.Options())
        {
            command.Add(option);
        }

        command.SetAction(parseResult => RunAsync(new CommandLineReader(parseResult), options, output));
        return command;
    }

    private static async Task<int> RunAsync(CommandLineReader reader, BundleOptions options, IRunOutput output)
    {
        // Configuration is read through one file access for the whole process: the profile search, the grammar
        // inventory and the container all see the same filesystem, and none of them opens a file directly.
        var fileProviderDetail = new PhysicianPlutusFileInfo();
        var files = new FileProviderAccessFactory(fileProviderDetail).CreateForWorkingDirectory();

        var profileResult = ReadProfile(reader, options, files, fileProviderDetail, output);
        if (!profileResult.Succeeded)
        {
            output.Error(profileResult.Error!);
            return 1;
        }

        var resolution = new RunOptionsResolver().Resolve(
            Request(reader, options),
            profileResult.Profile,
            profileResult.Path);

        if (!resolution.Succeeded)
        {
            output.Error(resolution.Error!);
            return 1;
        }

        var resolved = resolution.Options!;
        var validationErrors = resolved.Validate();
        if (validationErrors.Count > 0)
        {
            foreach (var error in validationErrors)
            {
                output.Error(error);
            }

            return 1;
        }

        WarnAboutIdleCommentOptions(resolved, output);

        // Resolve the strategy before any work: asking for ANTLR token channels without a maintained grammar is an
        // error, and it must not quietly degrade into lexical scanning.
        if (resolved.StripComments || resolved.Minify)
        {
            var grammarLoader = new GrammarInventoryLoader(files);
            if (!new CommentStrippingPlanner().TryResolve(resolved.CommentStrategy, grammarLoader.Discover(), out var plan, out var planError))
            {
                output.Error(planError!);
                return 1;
            }

            resolved = resolved with { CommentStripPlan = plan };
        }

        try
        {
            return await BundleRunHost.RunAsync(resolved, output, files, profileResult.Configuration);
        }
        catch (Exception exception)
        {
            output.Error(exception.Message);
            return 1;
        }
    }

    /// <summary>
    /// Finds and merges the profiles before anything runs: a profile that cannot be trusted is a failure, not a
    /// reason to fall back to the defaults without saying so. The command line flags are resolved separately and
    /// override the profile later.
    /// </summary>
    private static ProfileConfigurationResult ReadProfile(
        CommandLineReader reader,
        BundleOptions options,
        FileProviderAccess files,
        IFileInfoDetailProvider detailProvider,
        IRunOutput output)
    {
        var rawEnv = Environment.GetEnvironmentVariable(PlutusProfileLoader.PathVariable);
        var fromEnv = string.IsNullOrWhiteSpace(rawEnv) ? [] : rawEnv
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        var fromCli = reader.Value(options.Profile) ?? [];

        var named = fromCli.Count > 0 ? fromCli : fromEnv;
        var noProfile = reader.Value(options.NoProfile);
        if (noProfile && named.Count > 0)
        {
            output.Warning("--no-profile ignores --profile and the PLUTUS_PROFILE environment variable.");
            named = [];
        }
        else if (fromCli.Count > 0 && fromEnv.Count > 0)
        {
            output.Warning("The --profile parameter is specified, but the PLUTUS_PROFILE environment variable is being ignored.");
        }

        var result = new PlutusConfigurationBuilder(files).Build(detailProvider.GetCurrentDirectory(), named, !noProfile);
        if (!result.Succeeded)
        {
            return new ProfileConfigurationResult { Error = result.Error };
        }

        var profile = new ConfigurationOptionsBinder().Bind(result.Configuration, result.Layers);

        var profilePath = named.Count > 0 ? Path.Combine(detailProvider.GetCurrentDirectory(), $"{named[^1]}.plutusrc") : null;

        return new ProfileConfigurationResult
        {
            Configuration = result.Configuration,
            Profile = profile,
            Path = profilePath,
        };
    }

    /// <summary>
    /// What the command line stated, with everything else left unstated so the profile can fill it in. A flag that is
    /// not listed here cannot come from a profile because another source already owns it.
    /// </summary>
    private static RunOptionsRequest Request(CommandLineReader reader, BundleOptions options)
    {
        var diffOnly = reader.Value(options.DiffOnly);
        var diffFrom = reader.Value(options.DiffFrom);

        var archiveExtraction = BuildArchiveExtractionRequest(reader, options);

        return new RunOptionsRequest
        {
            ProjectPath = reader.Argument(options.ProjectPath),
            OutputPath = reader.Explicit(options.Output),
            UseGitignore = reader.ExplicitInverse(options.NoGitignore),
            Exclude = reader.Explicit(options.Exclude),
            UseDefaultExcludes = reader.ExplicitValue(options.UseDefaultExcludes),
            AllowDangerousFiles = reader.ExplicitValue(options.AllowDangerousFiles),
            MaxFileSize = reader.ExplicitValue(options.MaxFileSize),
            ExcludeDirs = reader.Explicit(options.ExcludeDirs),
            ExcludeFiles = reader.Explicit(options.ExcludeFiles),
            IncludeDirs = reader.Explicit(options.IncludeDirs),
            IncludeFiles = reader.Explicit(options.IncludeFiles),
            Encoding = reader.Explicit(options.Encoding),
            MaxConcurrency = reader.ExplicitValue(options.MaxConcurrency),
            Format = reader.Explicit(options.Format),
            MaxTokens = reader.Value(options.MaxTokens),
            TokenOverflowPolicy = reader.ExplicitValue(options.OverflowPolicy),
            TokenSelectionStrategy = reader.ExplicitValue(options.SelectionStrategy),
            IncludeFileContents = diffOnly ? false : null,
            Minify = reader.ExplicitValue(options.Minify),
            StripComments = reader.ExplicitValue(options.StripComments),
            PreserveDocComments = reader.ExplicitValue(options.PreserveDocComments),
            CommentStrategy = reader.ExplicitValue(options.CommentStrategy),
            SignaturesOnly = reader.ExplicitValue(options.SignaturesOnly),
            Branch = reader.Value(options.Branch),
            Commit = reader.Value(options.Commit),
            Diff = diffOnly || !string.IsNullOrWhiteSpace(diffFrom)
                ? new DiffRequest
                {
                    From = diffFrom,
                    To = reader.Value(options.DiffTo),
                    Only = diffOnly,
                    IncludeUntracked = reader.Value(options.DiffIncludeUntracked),
                }
                : null,
            ArchiveExtraction = archiveExtraction,
            Timeout = BuildTimeoutOptions(reader, options),
        };
    }

    /// <summary>Builds the archive extraction request from CLI flags.</summary>
    private static ArchiveExtractionOptions? BuildArchiveExtractionRequest(
        CommandLineReader reader,
        BundleOptions options)
    {
        var maxDownloadSize = reader.ExplicitValue(options.MaxDownloadSize);
        var maxExtractedSize = reader.ExplicitValue(options.MaxExtractedSize);
        var maxFileCount = reader.ExplicitNullableValue(options.MaxArchiveFileCount);
        var maxDepth = reader.ExplicitNullableValue(options.MaxArchiveDepth);
        var rejectDuplicates = reader.ExplicitValue(options.RejectDuplicateArchivePaths);
        var rejectSymlinks = reader.ExplicitValue(options.RejectSymlinksInArchives);

        // If nothing was explicitly set, return null so the profile/default path takes over.
        if (maxDownloadSize is null && maxExtractedSize is null && maxFileCount is null &&
            maxDepth is null && rejectDuplicates is null && rejectSymlinks is null)
        {
            return null;
        }

        return new ArchiveExtractionOptions
        {
            MaxDownloadSize = maxDownloadSize,
            MaxExtractedSize = maxExtractedSize,
            MaxFileCount = maxFileCount,
            MaxDepth = maxDepth,
            RejectDuplicatePaths = rejectDuplicates,
            RejectSymlinks = rejectSymlinks,
        };
    }

    /// <summary>Builds the timeout and retry configuration from CLI flags.</summary>
    private static TimeoutOptions BuildTimeoutOptions(CommandLineReader reader, BundleOptions options)
    {
        var httpTimeout = reader.Value(options.HttpTimeout);
        var maxRetries = reader.Value(options.MaxRetries);
        var retryBaseDelay = reader.Value(options.RetryBaseDelay);
        var retryMaxDelay = reader.Value(options.RetryMaxDelay);

        return new TimeoutOptions
        {
            OperationTimeout = httpTimeout,
            MaxRetries = maxRetries,
            RetryBaseDelay = retryBaseDelay,
            RetryMaxDelay = retryMaxDelay,
        };
    }

    /// <summary>
    /// The two comment switches do nothing on their own; saying so is better than accepting them in silence. The
    /// values are the resolved ones, so a profile that sets them is reported the same way a flag is.
    /// </summary>
    private static void WarnAboutIdleCommentOptions(RunAppOptions options, IRunOutput output)
    {
        if (options.StripComments || options.Minify)
        {
            return;
        }

        if (options.PreserveDocumentationComments)
        {
            output.Warning("--preserve-doc-comments has no effect without --strip-comments or --minify.");
        }

        if (options.CommentStrategy != CommentStrategyRequest.Auto)
        {
            output.Warning("--comment-strategy has no effect without --strip-comments or --minify.");
        }
    }
}
