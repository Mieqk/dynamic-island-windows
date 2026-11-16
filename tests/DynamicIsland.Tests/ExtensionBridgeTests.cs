using DynamicIsland.Services;

namespace DynamicIsland.Tests;

public class ExtensionBridgeTests
{
    [Theory]
    [InlineData("chrome-extension://abcdefghijklmnopabcdefghijklmnop")]
    [InlineData("moz-extension://1b2c3d4e-aaaa-bbbb-cccc-0123456789ab")]
    public void Browser_extension_origins_are_accepted(string origin) =>
        Assert.True(ExtensionBridge.IsExtensionOrigin(origin));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("https://evil.example")]
    [InlineData("http://localhost:47811")]
    [InlineData("chrome-extension://short")]                                      // too short to be an id
    [InlineData("chrome-extension://ABCDEFGHIJKLMNOP")]                           // ids are lower-case
    [InlineData("chrome-extension://abcdefghijklmnop/page.html")]                 // an origin has no path
    [InlineData("chrome-extension://abcdefghijklmnop.evil.example")]
    [InlineData("https://chrome-extension://abcdefghijklmnop")]
    [InlineData("safari-web-extension://abcdefghijklmnop")]
    public void Anything_else_is_refused(string? origin) =>
        Assert.False(ExtensionBridge.IsExtensionOrigin(origin));

    [Fact]
    public void An_overlong_origin_is_refused() =>
        Assert.False(ExtensionBridge.IsExtensionOrigin("chrome-extension://" + new string('a', 200)));
}
