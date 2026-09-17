using Etp.Reporting.Desktop;

namespace Etp.Reporting.Desktop.Tests;

public sealed class DesktopProductVersionTests
{
    [Theory]
    [InlineData("1.8.8+71a1aa2f3c4d5e6a7b8c9d0e1f2a3b4c5d6e7f80", "1.8.8 (71a1aa2)")]
    [InlineData("1.8.8+abc", "1.8.8 (abc)")]
    [InlineData("1.8.8", "1.8.8")]
    [InlineData("1.8.8+", "1.8.8")]
    public void Describe_shows_the_release_and_a_short_build(string informational, string expected)
        => Assert.Equal(expected, DesktopProductVersion.Describe(informational));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+71a1aa2")]
    public void Describe_never_returns_an_empty_title_fragment(string? informational)
        => Assert.Equal("(version unavailable)", DesktopProductVersion.Describe(informational));

    [Fact]
    public void Window_title_carries_the_product_name_and_the_running_build()
    {
        Assert.StartsWith("ETP Reporting Engine ", DesktopProductVersion.WindowTitle);
        Assert.EndsWith(DesktopProductVersion.Display, DesktopProductVersion.WindowTitle);
        Assert.False(string.IsNullOrWhiteSpace(DesktopProductVersion.Display));
        // The build actually under test must be identifiable from the title.
        Assert.NotEqual("(version unavailable)", DesktopProductVersion.Display);
    }
}
