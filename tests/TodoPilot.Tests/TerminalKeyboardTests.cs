namespace TodoPilot.Tests;

public sealed class TerminalKeyboardTests
{
    [Theory]
    [InlineData(ConsoleKey.Q, '\0', false, false, false, TodoListKeyAction.Quit)]
    [InlineData(ConsoleKey.R, '\0', false, false, false, TodoListKeyAction.Refresh)]
    [InlineData(ConsoleKey.UpArrow, '\0', false, false, false, TodoListKeyAction.FocusPrevious)]
    [InlineData(ConsoleKey.K, 'k', false, false, false, TodoListKeyAction.FocusPrevious)]
    [InlineData(ConsoleKey.DownArrow, '\0', false, false, false, TodoListKeyAction.FocusNext)]
    [InlineData(ConsoleKey.J, 'j', false, false, false, TodoListKeyAction.FocusNext)]
    [InlineData(ConsoleKey.PageUp, '\0', false, false, false, TodoListKeyAction.PagePrevious)]
    [InlineData(ConsoleKey.PageDown, '\0', false, false, false, TodoListKeyAction.PageNext)]
    [InlineData(ConsoleKey.Home, '\0', false, false, false, TodoListKeyAction.FocusFirst)]
    [InlineData(ConsoleKey.End, '\0', false, false, false, TodoListKeyAction.FocusLast)]
    [InlineData(ConsoleKey.Enter, '\n', false, false, false, TodoListKeyAction.ToggleExpanded)]
    [InlineData(ConsoleKey.Spacebar, ' ', false, false, false, TodoListKeyAction.ToggleExpanded)]
    [InlineData(ConsoleKey.X, 'x', false, false, true, TodoListKeyAction.SwitchSession)]
    [InlineData(ConsoleKey.Escape, '\u001b', false, false, false, TodoListKeyAction.None)]
    public void MapTodoListKey_NormalizesSupportedKeys(ConsoleKey consoleKey, char keyChar, bool shift, bool alt, bool control, TodoListKeyAction expected)
    {
        var key = new ConsoleKeyInfo(keyChar, consoleKey, shift, alt, control);

        Assert.Equal(expected, TerminalKeyboard.MapTodoListKey(key));
    }

    [Fact]
    public void MapSessionSelectionKey_KeepsEscapeAsNoOp()
    {
        var key = new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, shift: false, alt: false, control: false);

        Assert.Equal(SessionSelectionKeyAction.None, TerminalKeyboard.MapSessionSelectionKey(key));
    }

    [Fact]
    public void MapSessionSelectionKey_MapsCtrlUToToggleSessionIds()
    {
        var key = new ConsoleKeyInfo('u', ConsoleKey.U, shift: false, alt: false, control: true);

        Assert.Equal(SessionSelectionKeyAction.ToggleSessionIds, TerminalKeyboard.MapSessionSelectionKey(key));
    }

    [Fact]
    public void MapSessionSelectionKey_MapsCtrlAToToggleRunningOnly()
    {
        var key = new ConsoleKeyInfo('a', ConsoleKey.A, shift: false, alt: false, control: true);

        Assert.Equal(SessionSelectionKeyAction.ToggleRunningOnly, TerminalKeyboard.MapSessionSelectionKey(key));
    }

    [Fact]
    public void MapSessionSelectionKey_MapsPrintableCharactersToAppendFilter()
    {
        var key = new ConsoleKeyInfo('x', ConsoleKey.X, shift: false, alt: false, control: false);

        Assert.Equal(SessionSelectionKeyAction.AppendFilter, TerminalKeyboard.MapSessionSelectionKey(key));
    }
}
