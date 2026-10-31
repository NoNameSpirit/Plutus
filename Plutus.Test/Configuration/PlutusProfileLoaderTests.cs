using FluentAssertions;
using Plutus.Domain;
using Plutus.Domain.Comments;
using Plutus.Domain.Configuration;
using Plutus.Domain.Tokens;
using Plutus.Test.Fakes;

namespace Plutus.Test.Configuration;

/// <summary>
/// The profile file is the one place where a repository states its defaults, so reading it is pinned here: what each
/// key binds to, and which mistakes are refused instead of silently ignored. The tree lives in memory, so a test
/// describes what it wants to read rather than creating it on disk.
/// </summary>
public class PlutusProfileLoaderTests
{
    /// <summary>The host path the in-memory provider pretends to be rooted at; only string operations use it.</summary>
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\plutus-profile-tests" : "/plutus-profile-tests";

    private const string FullProfile = """
        {
          // A repository preset: every bound key, with comments and a trailing comma on purpose.
          "includeFiles": ["**/*.cs"],
          "includeDirs": ["src", "tests"],
          "includeExtensions": [".cs", "ts", "*.razor"],
          "exclude": ["*.log"],
          "excludeDirs": ["bin", "obj", "node_modules"],
          "excludeFiles": ["*.g.cs"],
          "useGitignore": true,
          "useDefaultExcludes": true,
          "allowDangerousFiles": false,
          "output": "artifacts/context.md",
          "format": "json",
          "encoding": "utf-8",
          "maxFileSize": "2M",
          "maxTokens": 200000,
          "tokenOverflowPolicy": "select",
          "tokenSelectionStrategy": "largest-first",
          "maxConcurrency": 4,
          "includeFileContents": true,
          "minify": true,
          "stripComments": true,
          "preserveDocComments": true,
          "commentStrategy": "lexical",
          "signaturesOnly": true,
        }
        """;

    [Fact]
    public void Load_BindsEveryKey()
    {
        var loader = Loader(new InMemoryFileProvider().AddFile(".plutusrc", FullProfile));

        var result = loader.Load(At(".plutusrc"));

        result.Succeeded.Should().BeTrue(result.Error);
        result.Path.Should().Be(At(".plutusrc"));
        var profile = result.Profile!;
        profile.IncludeFiles.Should().Equal("**/*.cs");
        profile.IncludeDirs.Should().Equal("src", "tests");
        profile.IncludeExtensions.Should().Equal(".cs", "ts", "*.razor");
        profile.Exclude.Should().Equal("*.log");
        profile.ExcludeDirs.Should().Equal("bin", "obj", "node_modules");
        profile.ExcludeFiles.Should().Equal("*.g.cs");
        profile.UseGitignore.Should().BeTrue();
        profile.UseDefaultExcludes.Should().BeTrue();
        profile.AllowDangerousFiles.Should().BeFalse();
        profile.Output.Should().Be("artifacts/context.md");
        profile.Format.Should().Be("json");
        profile.Encoding.Should().Be("utf-8");
        profile.MaxFileSize.Should().Be("2M");
        profile.MaxTokens.Should().Be(200000);
        profile.TokenOverflowPolicy.Should().Be(TokenOverflowPolicy.Select);
        profile.TokenSelectionStrategy.Should().Be(TokenSelectionStrategy.LargestFirst);
        profile.MaxConcurrency.Should().Be(4);
        profile.IncludeFileContents.Should().BeTrue();
        profile.Minify.Should().BeTrue();
        profile.StripComments.Should().BeTrue();
        profile.PreserveDocComments.Should().BeTrue();
        profile.CommentStrategy.Should().Be(CommentStrategyRequest.Lexical);
        profile.SignaturesOnly.Should().BeTrue();
    }

    [Fact]
    public void Load_LeavesAnUnstatedKeyUnstated_InsteadOfGuessingADefault()
    {
        var loader = Loader(new InMemoryFileProvider().AddFile(".plutusrc", """{ "excludeDirs": ["bin"] }"""));

        var profile = loader.Load(At(".plutusrc")).Profile!;

        profile.ExcludeDirs.Should().Equal("bin");
        profile.Format.Should().BeNull("a profile states only what it changes");
        profile.MaxConcurrency.Should().BeNull();
        profile.Minify.Should().BeNull();
    }

    [Fact]
    public void Load_AcceptsAPlainNameAndAJsonName()
    {
        var loader = Loader(new InMemoryFileProvider().AddFile(".plutusrc.json", """{ "format": "xml" }"""));

        loader.Load(At(".plutusrc.json")).Profile!.Format.Should().Be("xml");
    }

    [Fact]
    public void Load_RejectsAnUnknownKey_SoATypoIsNotSilentlyIgnored()
    {
        var loader = Loader(new InMemoryFileProvider().AddFile(".plutusrc", """{ "excludeDir": ["bin"] }"""));

        var result = loader.Load(At(".plutusrc"));

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain(At(".plutusrc")).And.Contain("'excludeDir'");
        result.Error.Should().Contain("excludeDirs", "the message names the keys that would have worked");
    }

    [Fact]
    public void Load_RejectsAValueOfTheWrongType()
    {
        var loader = Loader(new InMemoryFileProvider().AddFile(".plutusrc", """{ "maxTokens": "many" }"""));

        var result = loader.Load(At(".plutusrc"));

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain(At(".plutusrc")).And.Contain("maxTokens");
    }

    [Fact]
    public void Load_RejectsMalformedJson()
    {
        var loader = Loader(new InMemoryFileProvider().AddFile(".plutusrc", "{ not json }"));

        var result = loader.Load(At(".plutusrc"));

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("not a valid profile");
    }

    [Fact]
    public void Load_ReportsAMissingFile()
    {
        var loader = Loader(new InMemoryFileProvider());

        var result = loader.Load(At("absent.plutusrc"));

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("cannot read profile");
    }

    [Theory]
    [InlineData("""{ "maxTokens": 0 }""", "maxTokens")]
    [InlineData("""{ "maxConcurrency": 0 }""", "maxConcurrency")]
    [InlineData("""{ "maxFileSize": "2 furlongs" }""", "maxFileSize")]
    [InlineData("""{ "includeExtensions": ["src/*.cs"] }""", "includeExtensions")]
    [InlineData("""{ "includeExtensions": ["  "] }""", "includeExtensions")]
    public void Load_RejectsAValueItCannotUse(string json, string key)
    {
        var loader = Loader(new InMemoryFileProvider().AddFile(".plutusrc", json));

        var result = loader.Load(At(".plutusrc"));

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain(At(".plutusrc")).And.Contain(key);
    }

    [Fact]
    public void Locate_FindsTheNearestProfileWalkingUpTheTree()
    {
        var loader = Loader(new InMemoryFileProvider().AddFile("repository/.plutusrc", """{ "format": "json" }"""));

        loader.Locate(At("repository/src/feature/deep")).Should().Be(At("repository/.plutusrc"));
    }

    [Fact]
    public void Locate_PrefersTheCloserProfile()
    {
        var loader = Loader(new InMemoryFileProvider()
            .AddFile("repository/.plutusrc", """{ "format": "json" }""")
            .AddFile("repository/module/.plutusrc", """{ "format": "xml" }"""));

        loader.Locate(At("repository/module")).Should().Be(At("repository/module/.plutusrc"));
    }

    [Fact]
    public void Locate_DoesNotClimbAboveTheRepositoryRoot()
    {
        // A profile kept next to the repository belongs to whatever is above it, not to this project.
        var loader = Loader(new InMemoryFileProvider()
            .AddFile("workspace/.plutusrc", """{ "format": "json" }""")
            .AddDirectory("workspace/repository/.git")
            .AddDirectory("workspace/repository/src"));

        loader.Locate(At("workspace/repository/src")).Should().BeNull();
    }

    private static PlutusProfileLoader Loader(InMemoryFileProvider provider) =>
        new(new FileProviderAccess(provider, Root));

    private static string At(string relativePath) =>
        Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
