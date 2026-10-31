using System.CommandLine;
using System.CommandLine.Parsing;
using Plutus.Domain;
using Plutus.Domain.Comments;
using Plutus.Domain.Retry;
using Plutus.Domain.Tokens;

namespace Plutus.CLI;

/// <summary>
/// The surface of the bundling command: its argument and every option, defined once and named, so the command itself
/// only lists them and the action reads them through <see cref="CommandLineReader"/> instead of juggling locals.
/// </summary>
internal sealed class BundleOptions
{
    public Argument<List<string>> ProjectPath { get; } = new("project-path")
    {
        Description = "Path to the target project directory or GitHub repository URL.",
        Arity = ArgumentArity.OneOrMore,
    };

    public Option<string?> Output { get; } = new("--output")
    {
        Description = "Output bundle file. Default: <folder-name>_bundle.md"
    };

    public Option<List<string>?> Profile { get; } = new("--profile")
    {
        Description = "Path to a .plutusrc profile. Repeat to layer profiles: later ones override earlier ones. Overrides the PLUTUS_PROFILE environment variable.",
        Arity = ArgumentArity.OneOrMore,
        AllowMultipleArgumentsPerToken = true
    };

    public Option<bool> NoProfile { get; } = new("--no-profile")
    {
        Description = "Ignore any .plutusrc profile (and PLUTUS_PROFILE) and run with the command line and built-in defaults only."
    };

    public Option<FileSize> MaxFileSize { get; } = new("--max-file-size")
    {
        Description = "Max file size to include (e.g., 512K, 1M, 2MB).",
        DefaultValueFactory = _ => FileSize.Parse("1M"),
        CustomParser = FileSizeParser,
    };

    public Option<bool> NoGitignore { get; } = new("--no-gitignore")
    {
        Description = "Do not read root .gitignore"
    };

    public Option<string?> Branch { get; } = new("--branch")
    {
        Description = "Remote branch or tag to download. Cannot be used together with --commit."
    };

    public Option<string?> Commit { get; } = new("--commit")
    {
        Description = "Specific remote commit hash to download. Cannot be used together with --branch.",
    };

    public Option<List<string>> Exclude { get; } = new("--exclude")
    {
        Description = "Global gitignore-style wildcard patterns to exclude BOTH files and directories. Can be repeated",
        AllowMultipleArgumentsPerToken = true
    };

    public Option<List<string>> ExcludeFiles { get; } = new("--exclude-files")
    {
        Description = "Gitignore-style wildcard patterns targeting strictly FILES for exclusion. Useful for ignoring specific file names or extensions across the entire project graph",
        AllowMultipleArgumentsPerToken = true
    };

    public Option<List<string>> ExcludeDirs { get; } = new("--exclude-dirs")
    {
        Description = "Gitignore-style wildcard patterns targeting strictly DIRECTORIES for exclusion. Matches the target directory name or absolute tree path at any nesting level",
        AllowMultipleArgumentsPerToken = true
    };

    public Option<List<string>> IncludeFiles { get; } = new("--include-files")
    {
        Description = "Inverted gitignore-style filters. Explicitly isolates and restricts processing strictly to FILES that match these patterns. When active, all other unmatched files are omitted by default",
        AllowMultipleArgumentsPerToken = true
    };

    public Option<List<string>> IncludeDirs { get; } = new("--include-dirs")
    {
        Description = "Inverted gitignore-style filters. Explicitly restricts scanning paths strictly to DIRECTORIES that match these patterns. Allows you to whitelist and isolate processing to specific module trees",
        AllowMultipleArgumentsPerToken = true
    };

    public Option<bool> UseDefaultExcludes { get; } = new("--use-default-excludes")
    {
        Description = "Use default built-in junk exclusions.",
        DefaultValueFactory = _ => true
    };

    public Option<bool> AllowDangerousFiles { get; } = new("--allow-dangerous-files")
    {
        Description = "Include files normally treated as dangerous (.env, keys, etc.)."
    };

    public Option<bool> Minify { get; } = new("--minify")
    {
        Description = "Minify source code (remove comments and extra whitespace) for supported languages before bundling."
    };

    public Option<bool> StripComments { get; } = new("--strip-comments")
    {
        Description = "Remove comments only, for languages with a maintained comment syntax. Whitespace, indentation and line endings are preserved, and a space is inserted when removing a comment would join two tokens."
    };

    public Option<bool> PreserveDocComments { get; } = new("--preserve-doc-comments")
    {
        Description = "Keep documentation comments (///, //!, /** */, /*! */) when stripping or minifying. Off by default: documentation comments are removed like any other comment."
    };

    public Option<CommentStrategyRequest> CommentStrategy { get; } = new("--comment-strategy")
    {
        Description = "How comments are located: auto (ANTLR token channels where a project-maintained grammar exists, lexical scanning elsewhere), lexical (language-aware character scanner), antlr (requires a project-maintained grammar; fails when none exists).",
        DefaultValueFactory = _ => CommentStrategyRequest.Auto
    };

    public Option<bool> SignaturesOnly { get; } = new("--signatures-only")
    {
        Description = "Reduce supported files to their signatures: a parse-tree pass replaces implementation bodies (method bodies, => expression bodies, property accessor bodies) with a structural terminator, keeping declarations, attributes, generics and constraints. Independent of --strip-comments and --minify."
    };

    public Option<string> Encoding { get; } = new("--encoding")
    {
        Description = "Target text encoding for reading, writing, and logging (e.g., utf-8, windows-1251, koi8-r).",
        DefaultValueFactory = _ => "utf-8"
    };

    public Option<int> MaxConcurrency { get; } = new("--max-concurrency")
    {
        Description = "Limit maximum concurrent file processing I/O tasks bound by a semaphore.",
        DefaultValueFactory = _ => 8,
    };

    public Option<string> Format { get; } = new("--format")
    {
        Description = "Target serialization configuration format architecture (md, json, xml).",
        DefaultValueFactory = _ => "md"
    };

    public Option<int?> MaxTokens { get; } = new("--max-tokens")
    {
        Description = "Target source-token budget using an offline heuristic.",
    };

    public Option<TokenOverflowPolicy> OverflowPolicy { get; } = new("--token-overflow-policy")
    {
        Description = "Budget overflow behavior: fail, warn, or select.",
        DefaultValueFactory = _ => TokenOverflowPolicy.Fail
    };

    public Option<TokenSelectionStrategy> SelectionStrategy { get; } = new("--token-selection-strategy")
    {
        Description = "Deterministic file selection strategy for policy 'select': path-order or largest-first.",
        DefaultValueFactory = _ => TokenSelectionStrategy.PathOrder,
    };

    public Option<bool> DiffOnly { get; } = new("--diff-only")
    {
        Description = "Emit the change itself instead of the source: the bundle keeps its header, the list of changed paths and the raw .diff, and omits all file contents. A raw .diff section is appended."
    };

    public Option<string?> DiffFrom { get; } = new("--diff-from")
    {
        Description = "Restrict the bundle to files changed relative to this branch, tag, or commit, using merge-base semantics, and keep their contents. Combine with --diff-to to compare two specific commits. Cannot be combined with --branch or --commit.",
    };

    public Option<string?> DiffTo { get; } = new("--diff-to")
    {
        Description = "Upper revision of an explicit diff range. Together with --diff-from it compares the two commits directly, so a specific commit or range can be bundled.",
    };

    public Option<bool> DiffIncludeUntracked { get; } = new("--diff-include-untracked")
    {
        Description = "With --diff-only or --diff-from, also cover files git does not track yet, excluding anything .gitignore covers. A real new-file diff is generated for them."
    };

    public Option<FileSize> MaxDownloadSize { get; } = new("--max-download-size")
    {
        Description = "Maximum allowed download size for remote archives (e.g., 50M, 100MB).",
        DefaultValueFactory = _ => FileSize.Parse("100M"),
        CustomParser = FileSizeParser,
    };

    public Option<FileSize> MaxExtractedSize { get; } = new("--max-extracted-size")
    {
        Description = "Maximum allowed total extracted size for remote archives (e.g., 200M, 500MB). Protects against decompression bombs.",
        DefaultValueFactory = _ => FileSize.Parse("500M"),
        CustomParser = FileSizeParser,
    };

    public Option<int?> MaxArchiveFileCount { get; } = new("--max-archive-file-count")
    {
        Description = "Maximum number of files allowed in a remote archive.",
        DefaultValueFactory = _ => 10_000,
    };

    public Option<int?> MaxArchiveDepth { get; } = new("--max-archive-depth")
    {
        Description = "Maximum nesting depth of directories in a remote archive.",
        DefaultValueFactory = _ => 30,
    };

    public Option<bool> RejectDuplicateArchivePaths { get; } = new("--reject-duplicate-archive-paths")
    {
        Description = "Reject remote archives containing duplicate entry paths.",
        DefaultValueFactory = _ => true
    };

    public Option<bool> RejectSymlinksInArchives { get; } = new("--reject-symlinks-in-archives")
    {
        Description = "Reject remote archives containing symlinks (stored as directory entries with non-zero length).",
        DefaultValueFactory = _ => true
    };

    public Option<TimeSpan> HttpTimeout { get; } = new("--http-timeout")
    {
        Description = "Maximum time allowed for HTTP operations (e.g., 30s, 2m).",
        DefaultValueFactory = _ => TimeSpan.FromMinutes(2),
        CustomParser = ParseTimeSpan,
    };

    public Option<int> MaxRetries { get; } = new("--max-retries")
    {
        Description = "Maximum number of retry attempts for transient network failures.",
        DefaultValueFactory = _ => 3
    };

    public Option<TimeSpan> RetryBaseDelay { get; } = new("--retry-base-delay")
    {
        Description = "Base delay for exponential backoff on retries (e.g., 500ms, 1s).",
        DefaultValueFactory = _ => TimeSpan.FromMilliseconds(500),
        CustomParser = ParseTimeSpan,
    };

    public Option<TimeSpan> RetryMaxDelay { get; } = new("--retry-max-delay")
    {
        Description = "Maximum delay between retries (e.g., 10s, 1m). Default: 30s.",
        DefaultValueFactory = _ => TimeSpan.FromSeconds(30),
        CustomParser = ParseTimeSpan,
    };

    /// <summary>Applies the aliases and accepted values that belong to the command-line surface rather than to a type.</summary>
    public BundleOptions WithAliases()
    {
        Output.Aliases.Add("-o");
        Exclude.Aliases.Add("-e");
        Minify.Aliases.Add("-m");
        return this;
    }

    /// <summary>Every option the command accepts, in the order the help lists it.</summary>
    public IEnumerable<Option> Options()
    {
        yield return Output;
        yield return Profile;
        yield return NoProfile;
        yield return MaxFileSize;
        yield return NoGitignore;
        yield return Branch;
        yield return Commit;
        yield return Exclude;
        yield return ExcludeFiles;
        yield return ExcludeDirs;
        yield return IncludeFiles;
        yield return IncludeDirs;
        yield return UseDefaultExcludes;
        yield return AllowDangerousFiles;
        yield return Minify;
        yield return StripComments;
        yield return PreserveDocComments;
        yield return CommentStrategy;
        yield return SignaturesOnly;
        yield return Encoding;
        yield return MaxConcurrency;
        yield return Format;
        yield return MaxTokens;
        yield return OverflowPolicy;
        yield return SelectionStrategy;
        yield return DiffOnly;
        yield return DiffFrom;
        yield return DiffTo;
        yield return DiffIncludeUntracked;
        yield return MaxDownloadSize;
        yield return MaxExtractedSize;
        yield return MaxArchiveFileCount;
        yield return MaxArchiveDepth;
        yield return RejectDuplicateArchivePaths;
        yield return RejectSymlinksInArchives;
        yield return HttpTimeout;
        yield return MaxRetries;
        yield return RetryBaseDelay;
        yield return RetryMaxDelay;
    }

    private static TimeSpan ParseTimeSpan(ArgumentResult result)
    {
        if (result.Tokens.Count != 1)
        {
            result.AddError($"{result.Argument.Name} should have 1 value");
            return default;
        }

        try
        {
            return TimeSpanParser.Parse(result.Tokens.First().Value);
        }
        catch (Exception exception)
        {
            result.AddError(exception.Message);
            return default;
        }
    }

    private static FileSize FileSizeParser(ArgumentResult result)
    {
        if (result.Tokens.Count != 1)
        {
            result.AddError($"{result.Argument.Name} should have 1 value");
            return default;
        }

        try
        {
            return FileSize.Parse(result.Tokens.First().Value);
        }
        catch (Exception exception)
        {
            result.AddError(exception.Message);
            return default;
        }
    }
}
