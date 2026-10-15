using PampaPad.Core;

namespace PampaPad.Tests;

public sealed class HotkeySourceEdgeTests : HotkeySourceFixture
{
    // ---- Las teclas rotas no toman su atajo (02 §10)

    [Theory]
    [InlineData("""{ "position": 0, "label": "A", "hotkey": "Ctrl+Alt+A" }""")]
    [InlineData("""{ "position": 13, "label": "A", "hotkey": "Ctrl+Alt+A" }""")]
    [InlineData("""{ "position": -1, "label": "A", "hotkey": "Ctrl+Alt+A" }""")]
    [InlineData("""{ "label": "A", "hotkey": "Ctrl+Alt+A" }""")]
    public void A_key_without_a_valid_position_does_not_take_its_hotkey(string broken)
    {
        IReadOnlyList<Problem> problems = Source.Apply(PageOf(broken + """, { "position": 2, "label": "B", "hotkey": "Ctrl+Alt+B" }"""), []);

        Assert.Empty(problems);
        Assert.Equal(new[] { "Ctrl+Alt+B" }, RegisteredTexts());
        Assert.Equal(new HotkeysState(1, 0), State.Current.Hotkeys);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    public void The_first_and_the_last_position_are_valid(int position)
    {
        Source.Apply(PageOf($$"""{ "position": {{position}}, "label": "A", "hotkey": "Ctrl+Alt+A" }"""), []);

        Assert.True(Windows.Press("Ctrl+Alt+A"));
        Assert.Equal($"key.{position}", Assert.Single(Events).Control);
    }

    [Fact]
    public void A_second_key_in_the_same_position_does_not_take_its_hotkey_and_the_first_keeps_its_own()
    {
        IReadOnlyList<Problem> problems = Source.Apply(
            PageOf("""{ "position": 5, "label": "A", "hotkey": "Ctrl+Alt+A" }, { "position": 5, "label": "B", "hotkey": "Ctrl+Alt+B" }, { "position": 6, "label": "C", "hotkey": "Ctrl+Alt+C" }"""),
            []);

        Assert.Empty(problems);
        Assert.Equal(new[] { "Ctrl+Alt+A", "Ctrl+Alt+C" }, RegisteredTexts());
        Assert.False(Windows.Press("Ctrl+Alt+B"));
        Windows.Press("Ctrl+Alt+A");
        Assert.Equal("key.5", Assert.Single(Events).Control);
    }

    [Fact]
    public void A_repeated_position_loses_its_hotkey_even_if_the_first_one_has_none()
    {
        Source.Apply(PageOf("""{ "position": 5, "label": "A" }, { "position": 5, "label": "B", "hotkey": "Ctrl+Alt+B" }"""), []);

        Assert.Empty(Windows.Registered);
    }

    // ---- Quién gana y qué se informa

    [Fact]
    public void When_a_page_key_and_a_general_hotkey_share_the_combination_the_page_key_wins()
    {
        IReadOnlyList<Problem> problems = Source.Apply(PageOf(Marks), General("""{ "hotkey": "Ctrl+Shift+Alt+F9", "action": "otra" }"""));

        Assert.Empty(problems);
        Assert.Equal(new HotkeysState(2, 0), State.Current.Hotkeys);
        Windows.Press("Ctrl+Shift+Alt+F9");
        Assert.Equal("key.5", Assert.Single(Events).Control);
    }

    [Fact]
    public void Rejections_are_counted_for_each_apply_and_a_combination_that_was_freed_is_taken_on_the_next_one()
    {
        Windows.Taken.Add(Parse("Ctrl+Shift+Alt+F6"));

        Source.Apply(PageOf(Marks), []);
        Assert.Equal(new HotkeysState(1, 1), State.Current.Hotkeys);
        Assert.Contains(Log.Recent(), entry => entry.Module == "hotkeys" && entry.Message == "Atajos tomados: 1; rechazados por Windows: 1.");

        Windows.Taken.Clear();
        Assert.Empty(Source.Apply(PageOf(Marks), []));
        Assert.Equal(new HotkeysState(2, 0), State.Current.Hotkeys);
        Assert.True(Windows.Press("Ctrl+Shift+Alt+F6"));
    }

    [Fact]
    public void Every_rejection_is_a_problem_and_the_count_matches()
    {
        Windows.Taken.Add(Parse("Ctrl+Shift+Alt+F6"));
        Windows.Taken.Add(Parse("Ctrl+Alt+D"));

        IReadOnlyList<Problem> problems = Source.Apply(PageOf(Marks), General("""{ "hotkey": "Ctrl+Alt+D", "action": "b" }"""));

        Assert.Equal(new[] { "hotkeys[0].hotkey", "keys[1].hotkey" }, problems.Select(problem => problem.Where).Order());
        Assert.Equal(new HotkeysState(1, 2), State.Current.Hotkeys);
    }

    [Fact]
    public void A_rejected_combination_is_not_asked_again_for_a_repeated_one_in_the_same_apply()
    {
        Windows.Taken.Add(Parse("Ctrl+Alt+D"));

        IReadOnlyList<Problem> problems = Source.Apply(
            PageOf("""{ "position": 1, "label": "A", "hotkey": "Ctrl+Alt+D" }, { "position": 2, "label": "B", "hotkey": "Ctrl+Alt+D" }"""), []);

        Assert.Equal("keys[0].hotkey", Assert.Single(problems).Where);
    }

    [Fact]
    public void Places_in_the_file_count_the_empty_elements()
    {
        Windows.Taken.Add(Parse("Ctrl+Alt+D"));

        Problem inPage = Assert.Single(Source.Apply(PageOf("""null, { "position": 2, "label": "A", "hotkey": "Ctrl+Alt+D" }"""), []));
        Assert.Equal("keys[1].hotkey", inPage.Where);

        Windows.Taken.Clear();
        Windows.Taken.Add(Parse("Ctrl+Alt+F"));
        Problem inList = Assert.Single(Source.Apply(new Page(), General("""null, { "hotkey": "Ctrl+Alt+S", "action": "a" }, { "hotkey": "Ctrl+Alt+F", "action": "b" }""")));
        Assert.Equal("hotkeys[2].hotkey", inList.Where);

        Windows.Press("Ctrl+Alt+S");
        Assert.Equal(new InputEvent("hotkeys", "hotkey", Gesture.Tap, 1), Assert.Single(Events));
    }
}
