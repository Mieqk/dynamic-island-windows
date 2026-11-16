using DynamicIsland.Services;

namespace DynamicIsland.Tests;

public class DedicationTests
{
    [Theory]
    [InlineData(@"C:\Users\x\Downloads\dedication.json", true)]
    [InlineData(@"C:\Users\x\Downloads\dedication (1).json", true)]
    [InlineData(@"C:\Users\x\Downloads\Dedication.JSON", true)]
    [InlineData(@"C:\Users\x\Downloads\notes.json", false)]
    [InlineData(@"C:\Users\x\Downloads\dedication.txt", false)]
    [InlineData(@"C:\Users\x\Downloads\my-dedication.json", false)]
    public void Recognises_the_message_file_by_its_name(string path, bool expected) =>
        Assert.Equal(expected, Dedication.LooksLikeDedicationFile(path));

    [Fact]
    public void Reads_the_words_and_the_special_dates()
    {
        using var tmp = new TempFolder();
        var file = tmp.File("dedication.json");
        File.WriteAllText(file, """
            {
              "title": "Имя",
              "toast": "Привет",
              "message": "Длинное сообщение",
              "signature": "Подпись",
              "themeName": "Тема",
              "versionLabel": "Atoll · для Имени",
              "specialDates": [
                { "date": "01-02", "title": "С днём рождения", "text": "Поздравление" },
                { "date": "", "title": "Без даты", "text": "Не показывается" }
              ]
            }
            """);

        var d = Dedication.Read(file);
        Assert.NotNull(d);
        Assert.Equal("Имя", d!.Title);
        Assert.Equal("Привет", d.Toast);
        Assert.Equal("Длинное сообщение", d.Message);
        Assert.Equal("Тема", d.ThemeName);
        Assert.Equal("Atoll · для Имени", d.VersionLabel);

        Assert.Equal("С днём рождения", d.Today(new DateTime(2027, 1, 2, 9, 0, 0))?.Title);
        Assert.Null(d.Today(new DateTime(2027, 1, 3)));
        Assert.Null(d.Today(new DateTime(2027, 2, 1)));   // "02-01" is not "01-02"
    }

    [Fact]
    public void A_file_without_special_dates_still_works()
    {
        using var tmp = new TempFolder();
        var file = tmp.File("dedication.json");
        File.WriteAllText(file, """{ "title": "Имя", "specialDates": null }""");

        var d = Dedication.Read(file);
        Assert.NotNull(d);
        Assert.Null(d!.Today(new DateTime(2027, 1, 2)));
    }

    [Fact]
    public void Nulls_in_the_file_are_read_as_empty()
    {
        using var tmp = new TempFolder();
        var file = tmp.File("dedication.json");
        File.WriteAllText(file, """
            { "title": "Имя", "toast": null, "message": null, "signature": null, "themeName": null, "versionLabel": null,
              "specialDates": [ null, { "date": null, "title": "x", "text": null } ] }
            """);

        var d = Dedication.Read(file);
        Assert.NotNull(d);
        Assert.Equal("", d!.Toast);
        Assert.Equal("", d.ThemeName);
        Assert.Single(d.SpecialDates);
        Assert.Null(d.Today(new DateTime(2027, 1, 2)));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{}")]
    [InlineData("""{ "themeName": "only a theme, no words" }""")]
    [InlineData("null")]
    public void A_broken_or_empty_file_is_not_used(string content)
    {
        using var tmp = new TempFolder();
        var file = tmp.File("dedication.json");
        File.WriteAllText(file, content);
        Assert.Null(Dedication.Read(file));
    }

    [Fact]
    public void A_missing_file_is_not_an_error()
    {
        using var tmp = new TempFolder();
        Assert.Null(Dedication.Read(tmp.File("nothing-here.json")));
    }
}
