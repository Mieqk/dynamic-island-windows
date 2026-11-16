using DynamicIsland.Services;

namespace DynamicIsland.Tests;

public class UsageCatalogTests
{
    [Theory]
    [InlineData("chrome", "Chrome", UsageCategory.Browser)]
    [InlineData("CHROME", "Chrome", UsageCategory.Browser)]
    [InlineData("WINWORD", "Word", UsageCategory.Work)]
    [InlineData("Code", "VS Code", UsageCategory.Work)]
    [InlineData("Telegram", "Telegram", UsageCategory.Communication)]
    [InlineData("Photoshop", "Photoshop", UsageCategory.Creative)]
    [InlineData("steam", "Steam", UsageCategory.Entertainment)]
    [InlineData("DynamicIsland", "Atoll", UsageCategory.Other)]
    public void Known_programs_get_a_name_and_a_category(string process, string name, UsageCategory category) =>
        Assert.Equal((name, category), UsageCatalog.Describe(process));

    [Fact]
    public void Unknown_programs_keep_their_name_and_go_to_other() =>
        Assert.Equal(("SomeTool", UsageCategory.Other), UsageCatalog.Describe("SomeTool"));

    [Fact]
    public void Overrides_win_and_bad_ones_are_ignored()
    {
        var overrides = new Dictionary<string, string> { ["chrome"] = "work", ["steam"] = "no-such-category" };

        Assert.Equal(("Chrome", UsageCategory.Work), UsageCatalog.Describe("Chrome", overrides));
        Assert.Equal(("Steam", UsageCategory.Entertainment), UsageCatalog.Describe("steam", overrides));
    }

    [Fact]
    public void Every_category_has_an_id_that_reads_back()
    {
        foreach (var category in Enum.GetValues<UsageCategory>())
            Assert.Equal(category, UsageCatalog.ParseCategory(UsageCatalog.IdOf(category)));
        Assert.Null(UsageCatalog.ParseCategory(null));
        Assert.Null(UsageCatalog.ParseCategory("???"));
    }
}
