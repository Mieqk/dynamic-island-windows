using DynamicIsland.Services;

namespace DynamicIsland.Tests;

public class TasksStoreTests
{
    private static readonly DateTime Morning = new(2026, 3, 10, 9, 0, 0);
    private const string App = "Daily Note";
    private const string Today = "2026-03-10";

    private static TasksStore New(TempFolder tmp) => new(tmp.File("tasks.json"));

    // ------------------------------------------------------------------ the time in the text

    [Theory]
    [InlineData("позвонить маме в 18:30", "позвонить маме", 18 * 60 + 30)]
    [InlineData("18:30 позвонить", "позвонить", 18 * 60 + 30)]
    [InlineData("встреча 9:05", "встреча", 9 * 60 + 5)]
    [InlineData("call at 7:00 please", "call please", 7 * 60)]
    [InlineData("купить 2 батона", "купить 2 батона", -1)]
    [InlineData("счёт 12:99", "счёт 12:99", -1)]
    [InlineData("в 25:00", "в 25:00", -1)]
    [InlineData("2.50 за штуку", "2.50 за штуку", -1)]
    [InlineData("в 7:30", "в 7:30", 7 * 60 + 30)]          // nothing but a time: the text is kept
    public void Reads_a_time_from_the_text(string typed, string text, int minutes) =>
        Assert.Equal((text, minutes), TasksStore.ParseTime(typed));

    // ------------------------------------------------------------------ the island's own tasks

    [Fact]
    public void Adds_ticks_and_counts()
    {
        using var tmp = new TempFolder();
        var store = New(tmp);
        var a = store.Add("  Написать отчёт  ", Morning);
        var b = store.Add("Позвонить в 18:30", Morning);
        Assert.Equal("Написать отчёт", a!.Text);
        Assert.Equal(-1, a.RemindAt);
        Assert.Equal("Позвонить", b!.Text);
        Assert.Equal(18 * 60 + 30, b.RemindAt);
        Assert.Equal((0, 2), store.Progress(Morning));

        Assert.True(store.Toggle(a.Id, Morning));
        Assert.Equal((1, 2), store.Progress(Morning));
        Assert.Equal(new[] { "Позвонить", "Написать отчёт" }, store.Today(Morning).Select(t => t.Text));   // unfinished first

        Assert.True(store.Toggle(a.Id, Morning));
        Assert.Equal((0, 2), store.Progress(Morning));
        Assert.False(store.Toggle("no-such-id", Morning));
    }

    [Fact]
    public void Empty_text_is_not_a_task_and_long_text_is_cut()
    {
        using var tmp = new TempFolder();
        var store = New(tmp);
        Assert.Null(store.Add("", Morning));
        Assert.Null(store.Add("   ", Morning));
        Assert.Equal(TasksStore.MaxText, store.Add(new string('я', 500), Morning)!.Text.Length);
    }

    [Fact]
    public void The_list_has_a_limit()
    {
        using var tmp = new TempFolder();
        var store = New(tmp);
        for (int i = 0; i < TasksStore.MaxTasks; i++) Assert.NotNull(store.Add("задача " + i, Morning));
        Assert.Null(store.Add("ещё одна", Morning));
    }

    [Fact]
    public void Removes_one_and_clears_the_finished()
    {
        using var tmp = new TempFolder();
        var store = New(tmp);
        var a = store.Add("a", Morning)!;
        var b = store.Add("b", Morning)!;
        var c = store.Add("c", Morning)!;
        store.Toggle(a.Id, Morning);
        store.Toggle(b.Id, Morning);

        Assert.True(store.Remove(c.Id));
        Assert.False(store.Remove(c.Id));
        Assert.Equal(2, store.ClearDone(Morning));
        Assert.Empty(store.Today(Morning));
    }

    [Fact]
    public void Tasks_survive_a_restart()
    {
        using var tmp = new TempFolder();
        var file = tmp.File("tasks.json");
        var store = new TasksStore(file);
        var a = store.Add("Полить цветы в 20:00", Morning)!;
        store.Toggle(a.Id, Morning);

        var again = new TasksStore(file).Today(Morning);
        var task = Assert.Single(again);
        Assert.Equal("Полить цветы", task.Text);
        Assert.True(task.Done);
        Assert.Equal(20 * 60, task.RemindAt);
        Assert.Equal(a.Id, task.Id);
    }

    [Fact]
    public void A_broken_file_is_an_empty_list()
    {
        using var tmp = new TempFolder();
        var file = tmp.File("tasks.json");
        File.WriteAllText(file, "{ broken");
        var store = new TasksStore(file);
        Assert.Empty(store.Today(Morning));
        Assert.NotNull(store.Add("после поломки", Morning));
    }

    [Fact]
    public void The_next_day_unfinished_tasks_move_on_and_finished_ones_go()
    {
        using var tmp = new TempFolder();
        var store = New(tmp);
        var open = store.Add("не успела", Morning)!;
        var done = store.Add("сделано", Morning)!;
        store.Toggle(done.Id, Morning);

        var tomorrow = Morning.AddDays(1);
        var list = store.Today(tomorrow);
        var task = Assert.Single(list);
        Assert.Equal(open.Id, task.Id);
        Assert.Equal("2026-03-11", task.Day);
        Assert.Empty(new TasksStore(tmp.File("tasks.json")).Today(tomorrow).Where(t => t.Text == "сделано"));
    }

    // ------------------------------------------------------------------ reminders

    [Fact]
    public void A_reminder_is_due_once_at_its_time()
    {
        using var tmp = new TempFolder();
        var store = New(tmp);
        store.Add("Позвонить в 18:30", Morning);
        store.Add("без времени", Morning);

        Assert.Empty(store.DueReminders(new DateTime(2026, 3, 10, 18, 29, 59)));
        var due = store.DueReminders(new DateTime(2026, 3, 10, 18, 30, 0));
        Assert.Equal("Позвонить", Assert.Single(due).Text);
        Assert.Empty(store.DueReminders(new DateTime(2026, 3, 10, 18, 31, 0)));   // said once
    }

    [Fact]
    public void A_finished_task_does_not_remind()
    {
        using var tmp = new TempFolder();
        var store = New(tmp);
        var t = store.Add("Позвонить в 18:30", Morning)!;
        store.Toggle(t.Id, Morning);
        Assert.Empty(store.DueReminders(new DateTime(2026, 3, 10, 19, 0, 0)));
    }

    [Fact]
    public void A_task_carried_to_the_next_day_reminds_again()
    {
        using var tmp = new TempFolder();
        var store = New(tmp);
        store.Add("Позвонить в 18:30", Morning);
        Assert.Single(store.DueReminders(new DateTime(2026, 3, 10, 18, 30, 0)));

        Assert.Empty(store.DueReminders(new DateTime(2026, 3, 11, 9, 0, 0)));
        Assert.Single(store.DueReminders(new DateTime(2026, 3, 11, 18, 30, 0)));
    }

    // ------------------------------------------------------------------ a linked app

    private static ExternalTask E(string id, string text, bool done = false) => new(id, text, done);

    [Fact]
    public void Mirrors_the_list_of_the_linked_app()
    {
        using var tmp = new TempFolder();
        var store = New(tmp);
        store.Add("своя задача", Morning);

        var actions = store.Sync(App, Today, [E("a", "Зарядка"), E("b", "Отчёт в 15:00", done: true)], Morning);
        Assert.Empty(actions);

        var list = store.Today(Morning);
        Assert.Equal(3, list.Count);
        var a = list.Single(t => t.ExternalId == "a");
        var b = list.Single(t => t.ExternalId == "b");
        Assert.Equal(App, a.Source);
        Assert.True(a.IsLinked);
        Assert.True(b.Done);
        Assert.Equal("Отчёт в 15:00", b.Text);          // the app's text is kept as it is
        Assert.Equal(15 * 60, b.RemindAt);              // the time is still read from it
        Assert.True(store.IsLinked(App, Morning.AddSeconds(30)));
        Assert.False(store.IsLinked(App, Morning.AddMinutes(3)));
        Assert.False(store.IsLinked("Other App", Morning));
    }

    [Fact]
    public void A_mirrored_task_keeps_its_id_between_syncs()
    {
        using var tmp = new TempFolder();
        var store = New(tmp);
        store.Sync(App, Today, [E("a", "Зарядка")], Morning);
        var first = store.Today(Morning).Single().Id;
        store.Sync(App, Today, [E("a", "Зарядка утром"), E("b", "Новая")], Morning.AddSeconds(4));

        var list = store.Today(Morning);
        Assert.Equal(first, list.Single(t => t.ExternalId == "a").Id);
        Assert.Equal("Зарядка утром", list.Single(t => t.ExternalId == "a").Text);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void A_list_for_another_day_is_ignored()
    {
        using var tmp = new TempFolder();
        var store = New(tmp);
        Assert.Empty(store.Sync(App, "2026-03-09", [E("a", "вчерашняя")], Morning));
        Assert.Empty(store.Today(Morning));
        Assert.False(store.IsLinked(App, Morning));
    }

    [Fact]
    public void A_tick_made_here_is_handed_to_the_app_once_and_stays_visible()
    {
        using var tmp = new TempFolder();
        var store = New(tmp);
        store.Sync(App, Today, [E("a", "Зарядка"), E("b", "Отчёт")], Morning);
        var a = store.Today(Morning).Single(t => t.ExternalId == "a");
        Assert.True(store.Toggle(a.Id, Morning));

        // the app still has the old state: it gets the tick, and the island keeps showing it
        var actions = store.Sync(App, Today, [E("a", "Зарядка"), E("b", "Отчёт")], Morning.AddSeconds(4));
        Assert.Equal(new TaskAction("toggle", "a", "", true), Assert.Single(actions));
        Assert.True(store.Today(Morning).Single(t => t.ExternalId == "a").Done);

        // the app applied it: nothing more to say
        actions = store.Sync(App, Today, [E("a", "Зарядка", done: true), E("b", "Отчёт")], Morning.AddSeconds(5));
        Assert.Empty(actions);
        Assert.True(store.Today(Morning).Single(t => t.ExternalId == "a").Done);
    }

    [Fact]
    public void If_the_app_does_not_apply_a_tick_its_list_wins()
    {
        using var tmp = new TempFolder();
        var store = New(tmp);
        store.Sync(App, Today, [E("a", "Зарядка")], Morning);
        store.Toggle(store.Today(Morning).Single().Id, Morning);
        store.Sync(App, Today, [E("a", "Зарядка")], Morning.AddSeconds(4));                    // handed over

        Assert.Empty(store.Sync(App, Today, [E("a", "Зарядка")], Morning.AddSeconds(8)));       // still not done there
        Assert.False(store.Today(Morning).Single().Done);
    }

    [Fact]
    public void Two_ticks_on_the_same_task_send_only_the_last_state()
    {
        using var tmp = new TempFolder();
        var store = New(tmp);
        store.Sync(App, Today, [E("a", "Зарядка")], Morning);
        var id = store.Today(Morning).Single().Id;
        store.Toggle(id, Morning);
        store.Toggle(id, Morning);

        var actions = store.Sync(App, Today, [E("a", "Зарядка")], Morning.AddSeconds(4));
        Assert.Equal(new TaskAction("toggle", "a", "", false), Assert.Single(actions));
    }

    [Fact]
    public void A_tick_on_a_task_the_app_deleted_is_dropped()
    {
        using var tmp = new TempFolder();
        var store = New(tmp);
        store.Sync(App, Today, [E("a", "Зарядка"), E("b", "Отчёт")], Morning);
        store.Toggle(store.Today(Morning).Single(t => t.ExternalId == "a").Id, Morning);

        Assert.Empty(store.Sync(App, Today, [E("b", "Отчёт")], Morning.AddSeconds(4)));
        Assert.Equal("b", store.Today(Morning).Single().ExternalId);
    }

    [Fact]
    public void A_task_added_here_for_the_app_shows_at_once_and_is_handed_over()
    {
        using var tmp = new TempFolder();
        var store = New(tmp);
        store.Sync(App, Today, [E("a", "Зарядка")], Morning);

        var added = store.Add("Купить хлеб в 19:00", Morning, App)!;
        Assert.True(added.IsLinked);
        Assert.Equal("Купить хлеб в 19:00", added.Text);      // the app gets the text as typed
        Assert.Equal(19 * 60, added.RemindAt);
        Assert.Equal(2, store.Today(Morning).Count);

        var actions = store.Sync(App, Today, [E("a", "Зарядка")], Morning.AddSeconds(4));
        var action = Assert.Single(actions);
        Assert.Equal("add", action.Type);
        Assert.Equal("Купить хлеб в 19:00", action.Text);
        Assert.False(action.Done);
        Assert.Equal(2, store.Today(Morning).Count);            // still shown while the app takes it in

        // the app now has it under its own id: no duplicate, nothing more to send
        actions = store.Sync(App, Today, [E("a", "Зарядка"), E("x", "Купить хлеб в 19:00")], Morning.AddSeconds(5));
        Assert.Empty(actions);
        var list = store.Today(Morning);
        Assert.Equal(2, list.Count);
        Assert.Equal("x", list.Single(t => t.Text.StartsWith("Купить")).ExternalId);
    }

    [Fact]
    public void A_new_task_ticked_before_the_app_saw_it_arrives_ticked()
    {
        using var tmp = new TempFolder();
        var store = New(tmp);
        store.Sync(App, Today, [], Morning);
        var added = store.Add("Уже сделано", Morning, App)!;
        store.Toggle(added.Id, Morning);

        var action = Assert.Single(store.Sync(App, Today, [], Morning.AddSeconds(4)));
        Assert.Equal("add", action.Type);
        Assert.True(action.Done);
    }

    [Fact]
    public void What_was_done_here_waits_for_the_app_across_a_restart()
    {
        using var tmp = new TempFolder();
        var file = tmp.File("tasks.json");
        var store = new TasksStore(file);
        store.Sync(App, Today, [E("a", "Зарядка")], Morning);
        store.Toggle(store.Today(Morning).Single().Id, Morning);

        var again = new TasksStore(file);
        Assert.True(again.Today(Morning).Single().Done);
        var action = Assert.Single(again.Sync(App, Today, [E("a", "Зарядка")], Morning.AddMinutes(10)));
        Assert.Equal(new TaskAction("toggle", "a", "", true), action);
    }

    [Fact]
    public void Linked_tasks_are_not_deleted_or_cleared_here()
    {
        using var tmp = new TempFolder();
        var store = New(tmp);
        store.Sync(App, Today, [E("a", "Зарядка", done: true)], Morning);
        var own = store.Add("своя", Morning)!;
        store.Toggle(own.Id, Morning);

        Assert.False(store.Remove(store.Today(Morning).Single(t => t.IsLinked).Id));
        Assert.Equal(1, store.ClearDone(Morning));
        Assert.Equal("a", store.Today(Morning).Single().ExternalId);
    }

    [Fact]
    public void The_next_day_yesterdays_mirror_and_queue_are_gone()
    {
        using var tmp = new TempFolder();
        var store = New(tmp);
        store.Sync(App, Today, [E("a", "Зарядка")], Morning);
        store.Toggle(store.Today(Morning).Single().Id, Morning);

        var tomorrow = Morning.AddDays(1);
        Assert.Empty(store.Today(tomorrow));
        Assert.Empty(store.Sync(App, "2026-03-11", [E("a", "Зарядка")], tomorrow));   // yesterday's tick is not replayed
        Assert.False(store.Today(tomorrow).Single().Done);
    }

    [Fact]
    public void Unlinking_drops_the_mirror()
    {
        using var tmp = new TempFolder();
        var store = New(tmp);
        store.Add("своя", Morning);
        store.Sync(App, Today, [E("a", "Зарядка")], Morning);
        store.Unlink(App);

        Assert.Equal("своя", store.Today(Morning).Single().Text);
        Assert.False(store.IsLinked(App, Morning));
    }

    [Fact]
    public void Garbage_from_the_app_is_skipped()
    {
        using var tmp = new TempFolder();
        var store = New(tmp);
        store.Sync(App, Today, [E("", "без id"), E("b", "   "), E("c", new string('я', 1000)), E("d", "нормальная")], Morning);

        var list = store.Today(Morning);
        Assert.Equal(2, list.Count);
        Assert.Equal(TasksStore.MaxText, list.Single(t => t.ExternalId == "c").Text.Length);
        Assert.Empty(store.Sync("", Today, [E("z", "без названия приложения")], Morning));
    }
}
