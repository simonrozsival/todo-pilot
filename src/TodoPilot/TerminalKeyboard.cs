namespace TodoPilot;

public enum TodoListKeyAction
{
    None,
    Quit,
    Refresh,
    FocusPrevious,
    FocusNext,
    PagePrevious,
    PageNext,
    FocusFirst,
    FocusLast,
    ToggleExpanded,
    SwitchSession
}

public enum SessionSelectionKeyAction
{
    None,
    Quit,
    Accept,
    Previous,
    Next,
    PagePrevious,
    PageNext,
    First,
    Last,
    Backspace,
    ToggleSessionIds,
    ToggleRunningOnly,
    AppendFilter
}

public static class TerminalKeyboard
{
    public static bool TryReadKey(out ConsoleKeyInfo key)
    {
        key = default;
        if (Console.IsInputRedirected)
        {
            return false;
        }

        try
        {
            if (!Console.KeyAvailable)
            {
                return false;
            }

            key = Console.ReadKey(intercept: true);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static TodoListKeyAction MapTodoListKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.X && key.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            return TodoListKeyAction.SwitchSession;
        }

        return key.Key switch
        {
            ConsoleKey.Q => TodoListKeyAction.Quit,
            ConsoleKey.R => TodoListKeyAction.Refresh,
            ConsoleKey.UpArrow or ConsoleKey.K => TodoListKeyAction.FocusPrevious,
            ConsoleKey.DownArrow or ConsoleKey.J => TodoListKeyAction.FocusNext,
            ConsoleKey.PageUp => TodoListKeyAction.PagePrevious,
            ConsoleKey.PageDown => TodoListKeyAction.PageNext,
            ConsoleKey.Home => TodoListKeyAction.FocusFirst,
            ConsoleKey.End => TodoListKeyAction.FocusLast,
            ConsoleKey.Enter or ConsoleKey.Spacebar => TodoListKeyAction.ToggleExpanded,
            _ => TodoListKeyAction.None
        };
    }

    public static SessionSelectionKeyAction MapSessionSelectionKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.U && key.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            return SessionSelectionKeyAction.ToggleSessionIds;
        }

        if (key.Key == ConsoleKey.A && key.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            return SessionSelectionKeyAction.ToggleRunningOnly;
        }

        return key.Key switch
        {
            ConsoleKey.Q => SessionSelectionKeyAction.Quit,
            ConsoleKey.Enter => SessionSelectionKeyAction.Accept,
            ConsoleKey.UpArrow or ConsoleKey.K => SessionSelectionKeyAction.Previous,
            ConsoleKey.DownArrow or ConsoleKey.J => SessionSelectionKeyAction.Next,
            ConsoleKey.PageUp => SessionSelectionKeyAction.PagePrevious,
            ConsoleKey.PageDown => SessionSelectionKeyAction.PageNext,
            ConsoleKey.Home => SessionSelectionKeyAction.First,
            ConsoleKey.End => SessionSelectionKeyAction.Last,
            ConsoleKey.Backspace => SessionSelectionKeyAction.Backspace,
            _ => !char.IsControl(key.KeyChar)
                ? SessionSelectionKeyAction.AppendFilter
                : SessionSelectionKeyAction.None
        };
    }
}
