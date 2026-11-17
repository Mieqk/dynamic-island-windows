using System.Text.Json.Nodes;
using DynamicIsland.Services;

namespace DynamicIsland.Tests;

/// <summary>/app/tasks/sync: what another app sends is read carefully, and the answer is plain JSON.</summary>
public class TasksBridgeTests
{
    private static JsonObject Json(string text) => (JsonObject)JsonNode.Parse(text)!;

    [Fact]
    public void Reads_the_day_and_the_tasks()
    {
        var req = ClaudeBridge.ParseTasksSync(Json("""
            { "app": "Daily Note", "day": "2026-03-10",
              "tasks": [ { "id": "a", "text": "Зарядка", "done": false }, { "id": "b", "text": "Отчёт", "done": true } ] }
            """));

        Assert.NotNull(req);
        Assert.Equal("Daily Note", req!.App);
        Assert.Equal("2026-03-10", req.Day);
        Assert.Equal(new[] { new ExternalTask("a", "Зарядка", false), new ExternalTask("b", "Отчёт", true) }, req.Tasks);
    }

    [Fact]
    public void Skips_what_is_not_a_task()
    {
        var req = ClaudeBridge.ParseTasksSync(Json("""
            { "day": "2026-03-10",
              "tasks": [ 5, "text", null, { "id": "", "text": "без id" }, { "id": "x" }, { "id": "ok", "text": "Нормальная", "done": "yes" } ] }
            """));

        Assert.Equal("App", req!.App);                                   // no name given
        Assert.Equal(new ExternalTask("ok", "Нормальная", false), Assert.Single(req.Tasks));   // "done" must be a real true
    }

    [Theory]
    [InlineData("""{ "tasks": [] }""")]
    [InlineData("""{ "day": "", "tasks": [] }""")]
    [InlineData("""{ "day": 20260310 }""")]
    public void Without_a_day_there_is_nothing_to_sync(string body) =>
        Assert.Null(ClaudeBridge.ParseTasksSync(Json(body)));

    [Fact]
    public void No_task_list_means_an_empty_list()
    {
        Assert.Empty(ClaudeBridge.ParseTasksSync(Json("""{ "day": "2026-03-10" }"""))!.Tasks);
        Assert.Empty(ClaudeBridge.ParseTasksSync(Json("""{ "day": "2026-03-10", "tasks": "nope" }"""))!.Tasks);
    }

    [Fact]
    public void Long_lists_and_long_texts_are_cut()
    {
        var tasks = string.Join(",", Enumerable.Range(0, 500).Select(i => $$"""{ "id": "t{{i}}", "text": "{{new string('я', 400)}}" }"""));
        var req = ClaudeBridge.ParseTasksSync(Json($$"""{ "day": "2026-03-10", "tasks": [ {{tasks}} ] }"""));

        Assert.Equal(TasksStore.MaxTasks, req!.Tasks.Count);
        Assert.All(req.Tasks, t => Assert.Equal(TasksStore.MaxText, t.Text.Length));
    }

    [Fact]
    public void The_answer_lists_the_actions()
    {
        var json = ClaudeBridge.TasksSyncResponse([new TaskAction("toggle", "a", "", true), new TaskAction("add", "tmp", "Купить хлеб", false)]);
        var actions = Json(json)["actions"]!.AsArray();

        Assert.Equal(2, actions.Count);
        Assert.Equal("toggle", actions[0]!["type"]!.GetValue<string>());
        Assert.Equal("a", actions[0]!["id"]!.GetValue<string>());
        Assert.True(actions[0]!["done"]!.GetValue<bool>());
        Assert.Equal("Купить хлеб", actions[1]!["text"]!.GetValue<string>());
        Assert.Equal("""{"actions":[]}""", ClaudeBridge.TasksSyncResponse([]));
    }

    [Fact]
    public void The_whole_round_trip_through_the_store()
    {
        using var tmp = new TempFolder();
        var store = new TasksStore(tmp.File("tasks.json"));
        var now = new DateTime(2026, 3, 10, 9, 0, 0);
        var first = ClaudeBridge.ParseTasksSync(Json("""{ "app": "Daily Note", "day": "2026-03-10", "tasks": [ { "id": "a", "text": "Зарядка", "done": false } ] }"""))!;

        Assert.Equal("""{"actions":[]}""", ClaudeBridge.TasksSyncResponse(store.Sync(first.App, first.Day, first.Tasks, now)));
        store.Toggle(store.Today(now).Single().Id, now);

        var answer = Json(ClaudeBridge.TasksSyncResponse(store.Sync(first.App, first.Day, first.Tasks, now.AddSeconds(4))));
        var action = Assert.Single(answer["actions"]!.AsArray());
        Assert.Equal("toggle", action!["type"]!.GetValue<string>());
        Assert.Equal("a", action["id"]!.GetValue<string>());
        Assert.True(action["done"]!.GetValue<bool>());
    }
}
