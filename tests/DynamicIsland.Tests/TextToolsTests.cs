using DynamicIsland.Services;

namespace DynamicIsland.Tests;

public class TextToolsTests
{
    [Theory]
    [InlineData("ghbdtn", "привет")]
    [InlineData("руддщ", "hello")]
    public void Fixes_text_typed_in_the_wrong_layout(string typed, string meant) =>
        Assert.Equal(meant, TextTools.SwapLayout(typed));

    [Fact]
    public void Transliterates_and_keeps_capitals_and_punctuation() =>
        Assert.Equal("Privet, Yulya!", TextTools.Transliterate("Привет, Юля!"));

    [Fact]
    public void Sentence_case_capitalises_after_full_stops() =>
        Assert.Equal("Привет. Как дела? Ок", TextTools.Sentence("ПРИВЕТ. как ДЕЛА? ок"));

    [Fact]
    public void Base64_and_url_coding_round_trip()
    {
        const string text = "Привет, мир & co?";
        Assert.Equal(text, TextTools.Base64Decode(TextTools.Base64Encode(text)));
        Assert.Equal(text, TextTools.UrlDecode(TextTools.UrlEncode(text)));
        Assert.Equal("a b c", TextTools.UrlDecode("a+b%20c"));
    }
}
