using System.CommandLine;
using FluentAssertions;
using Plutus.CLI;
using Plutus.Domain.Comments;

namespace Plutus.Test.Cli;

/// <summary>
/// The command line is read as "stated or not stated", because that is what decides whether a profile may fill a
/// value in. These tests pin that distinction, and the command surface a user sees.
/// </summary>
public class CommandLineReaderTests
{
    [Fact]
    public void Explicit_IsNullForAnOptionTheUserDidNotType_EvenWhenItHasADefault()
    {
        var (reader, options) = Parse(".");

        reader.Explicit(options.Format).Should().BeNull();
        reader.ExplicitValue(options.MaxConcurrency).Should().BeNull();
        reader.ExplicitValue(options.UseDefaultExcludes).Should().BeNull();
        reader.ExplicitValue(options.CommentStrategy).Should().BeNull();
    }

    [Fact]
    public void Explicit_ReturnsWhatTheUserTyped()
    {
        var (reader, options) = Parse(".", "--format", "json", "--max-concurrency", "2", "--comment-strategy", "lexical");

        reader.Explicit(options.Format).Should().Be("json");
        reader.ExplicitValue(options.MaxConcurrency).Should().Be(2);
        reader.ExplicitValue(options.CommentStrategy).Should().Be(CommentStrategyRequest.Lexical);
    }

    [Fact]
    public void ExplicitValue_ReportsAFlagThatWasTurnedOffExplicitly()
    {
        var (reader, options) = Parse(".", "--minify", "false");

        reader.ExplicitValue(options.Minify).Should().BeFalse("a typed flag overrides a profile, including with 'false'");
    }

    [Fact]
    public void ExplicitValue_ReportsAFlagAsTyped()
    {
        var (typed, options) = Parse(".", "--minify");
        var (omitted, _) = Parse(".");

        typed.ExplicitValue(options.Minify).Should().BeTrue();
        omitted.ExplicitValue(options.Minify).Should().BeNull();
    }

    [Fact]
    public void ExplicitInverse_ReadsTheNegatedFlag()
    {
        var (typed, options) = Parse(".", "--no-gitignore");
        var (omitted, _) = Parse(".");

        typed.ExplicitInverse(options.NoGitignore).Should().BeFalse("--no-gitignore means 'do not read .gitignore'");
        omitted.ExplicitInverse(options.NoGitignore).Should().BeNull();
    }

    [Fact]
    public void Value_ReportsTheDefaultForAnOptionThatWasNotTyped()
    {
        var (reader, options) = Parse(".");

        reader.Value(options.Format).Should().Be("md");
        reader.Value(options.MaxConcurrency).Should().Be(8);
        reader.Value(options.MaxTokens).Should().BeNull();
    }

    [Fact]
    public void Argument_ReportsTheProjectPaths()
    {
        var (reader, options) = Parse("one", "two");

        reader.Argument(options.ProjectPath).Should().Equal("one", "two");
    }

    [Fact]
    public void CommandSurface_OffersOneCommandPerJob()
    {
        var root = RootCommand();

        root.Subcommands.Select(command => command.Name).Should().Equal("bundle", "init");
        root.Options.Should().OnlyContain(option => option.Name.StartsWith("--help") || option.Name == "--version",
            "the bundling options belong to the bundle command, not to the root");
        root.Subcommands[0].Options.Should().Contain(option => option.Name == "--profile");
        root.Subcommands[0].Options.Should().Contain(option => option.Name == "--no-profile");
        root.Subcommands[1].Options.Select(option => option.Name).Should().Contain(["--output", "--force"]);
    }

    [Fact]
    public void Normalize_ReadsACommandLineWithoutACommandNameAsBundling()
    {
        // `plutus . --format md` predates the bundle command and keeps working; naming a command is the same thing.
        var root = RootCommand();

        Program.Normalize(root, ["."]).Should().Equal("bundle", ".");
        Program.Normalize(root, ["--format", "md", "."]).Should().Equal("bundle", "--format", "md", ".");
        Program.Normalize(root, ["bundle", "."]).Should().Equal("bundle", ".");
        Program.Normalize(root, ["init"]).Should().Equal("init");
    }

    [Fact]
    public void Normalize_LeavesTheCommandTreeItselfAlone()
    {
        var root = RootCommand();

        Program.Normalize(root, []).Should().BeEmpty();
        Program.Normalize(root, ["--help"]).Should().Equal("--help");
        Program.Normalize(root, ["--version"]).Should().Equal("--version");
    }

    private static RootCommand RootCommand()
    {
        var root = new RootCommand();
        root.Subcommands.Add(BundleCommand.Create(new RecordingRunOutput()));
        root.Subcommands.Add(InitCommand.Create(new RecordingRunOutput()));
        return root;
    }

    private static (CommandLineReader Reader, BundleOptions Options) Parse(params string[] args)
    {
        var options = new BundleOptions().WithAliases();
        var command = new RootCommand();
        command.Add(options.ProjectPath);
        foreach (var option in options.Options())
        {
            command.Add(option);
        }

        return (new CommandLineReader(command.Parse(args)), options);
    }

    [Fact]
    public void Parse_AcceptsMultipleProfilesInOneToken()
    {
        var (reader, options) = Parse(".", "--profile", "base", "review-diff");

        reader.Value(options.Profile).Should().Equal("base", "review-diff");
    }

    [Fact]
    public void Parse_AcceptsRepeatedProfileFlags()
    {
        var (reader, options) = Parse(".", "--profile", "base", "--profile", "ci");

        reader.Value(options.Profile).Should().Equal("base", "ci");
    }
}
