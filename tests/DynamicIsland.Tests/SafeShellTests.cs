using DynamicIsland.Services;

namespace DynamicIsland.Tests;

public class SafeShellTests
{
    [Theory]
    [InlineData("https://example.com/page?x=1")]
    [InlineData("http://example.com")]
    [InlineData("  https://example.com  ")]
    public void Web_addresses_may_be_opened(string url) => Assert.True(SafeShell.IsWebUrl(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData(@"C:\Windows\System32\calc.exe")]
    [InlineData("ms-settings:privacy")]
    [InlineData("io.vercel.dailynotes://auth")]
    [InlineData("ftp://example.com/file")]
    public void Programs_files_and_other_schemes_are_never_opened(string? text) => Assert.False(SafeShell.IsWebUrl(text));
}
