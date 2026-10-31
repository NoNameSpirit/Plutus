using FluentAssertions;
using Plutus.Domain;
using Plutus.Infrastructure.Configuration;
using Plutus.Test.Fakes;

namespace Plutus.Test.Configuration;

public class PlutusConfigurationBuilderTests
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\plutus-config-tests" : "/plutus-config-tests";

    [Fact]
    public void Build_WithoutNamedProfiles_AppliesTheBaseProfile()
    {
        var files = new InMemoryFileProvider().AddFile(".plutusrc", """{ "format": "json", "excludeDirs": ["bin"] }""");
        var filesAccess = new FileProviderAccess(files, Root);
        var builder = new PlutusConfigurationBuilder(filesAccess);

        var result = builder.Build(Root, []);

        result.Succeeded.Should().BeTrue(result.Error);

        var profile = new ConfigurationOptionsBinder().Bind(result.Configuration!, result.Layers);
        profile.Format.Should().Be("json");
        profile.ExcludeDirs.Should().Equal("bin");
    }

    [Fact]
    public void Build_WhenLaterProfileOverridesAString_KeepsTheLaterValue()
    {
        var files = new InMemoryFileProvider()
            .AddFile(".plutusrc", """{ "format": "md" }""")
            .AddFile(".petya.plutusrc", """{ "format": "json" }""");
        var builder = new PlutusConfigurationBuilder(new FileProviderAccess(files, Root));

        var result = builder.Build(Root, ["petya"]);
        result.Succeeded.Should().BeTrue(result.Error);

        var profile = new ConfigurationOptionsBinder().Bind(result.Configuration!, result.Layers);
        profile.Format.Should().Be("json");
    }

    [Fact]
    public void Build_WhenLaterProfileOverridesABool_KeepsTheLaterValue()
    {
        var files = new InMemoryFileProvider()
            .AddFile(".plutusrc", """{ "useGitignore": true }""")
            .AddFile(".petya.plutusrc", """{ "useGitignore": false }""");
        var builder = new PlutusConfigurationBuilder(new FileProviderAccess(files, Root));

        var result = builder.Build(Root, ["petya"]);
        result.Succeeded.Should().BeTrue(result.Error);

        var profile = new ConfigurationOptionsBinder().Bind(result.Configuration!, result.Layers);
        profile.UseGitignore.Should().BeFalse();
    }

    [Fact]
    public void Build_WhenLaterProfileAddsToAnArray_MergesBothArrays()
    {
        var files = new InMemoryFileProvider()
            .AddFile(".plutusrc", """{ "excludeDirs": ["bin", "obj"] }""")
            .AddFile(".petya.plutusrc", """{ "excludeDirs": ["dist"] }""");
        var builder = new PlutusConfigurationBuilder(new FileProviderAccess(files, Root));

        var result = builder.Build(Root, ["petya"]);
        result.Succeeded.Should().BeTrue(result.Error);

        var profile = new ConfigurationOptionsBinder().Bind(result.Configuration!, result.Layers);
        profile.ExcludeDirs.Should().BeEquivalentTo("bin", "obj", "dist");
    }


    [Fact]
    public void Build_WhenTwoProfilesAreNamed_LaterOneOverridesEarlier()
    {
        var files = new InMemoryFileProvider()
            .AddFile(".plutusrc", """{ "format": "md" }""")
            .AddFile(".base.plutusrc", """{ "format": "xml" }""")
            .AddFile(".review.plutusrc", """{ "format": "json" }""");
        var builder = new PlutusConfigurationBuilder(new FileProviderAccess(files, Root));

        var result = builder.Build(Root, ["base", "review"]);
        result.Succeeded.Should().BeTrue(result.Error);

        var profile = new ConfigurationOptionsBinder().Bind(result.Configuration!, result.Layers);
        profile.Format.Should().Be("json");
    }

    [Fact]
    public void Build_WhenNamedProfileIsMissing_ReturnsErrorNamingTheFile()
    {
        var files = new InMemoryFileProvider();
        var builder = new PlutusConfigurationBuilder(new FileProviderAccess(files, Root));

        var result = builder.Build(Root, ["missing"]);

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("'missing'");
        result.Error.Should().Contain(".missing.plutusrc");
    }
}
