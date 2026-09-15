using Briosa.Installer.Core;

namespace Briosa.Installer.Tests;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("0.1.0-review.4", "0.1.0-review.6")]
    [InlineData("1.9.0", "1.10.0")]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]
    [InlineData("1.0.0-rc.1", "1.0.0")]
    [InlineData("1.0.0-9", "1.0.0-A")]
    [InlineData("999999999999999999999.0.0", "1000000000000000000000.0.0")]
    public void OrdersReleasesWithoutLexicalOrIntegerShortcuts(string older, string newer)
    {
        Assert.True(ReleaseVersion.Compare(older, newer) < 0);
        Assert.True(ReleaseVersion.Compare(newer, older) > 0);
    }

    [Fact]
    public void BuildMetadataDoesNotAdvertiseAnotherUpdate() =>
        Assert.Equal(0, ReleaseVersion.Compare("1.0.0-review.6+abc", "1.0.0-review.6+def"));

    [Theory]
    [InlineData("development")]
    [InlineData("01.2.3")]
    [InlineData("1.2.3-01")]
    public void UnknownBuildVersionsCannotClaimUpdatePrecedence(string version)
    {
        Assert.False(ReleaseVersion.IsValid(version));
        Assert.Throws<ArgumentException>(() => ReleaseVersion.Compare(version, "1.0.0"));
    }
}
