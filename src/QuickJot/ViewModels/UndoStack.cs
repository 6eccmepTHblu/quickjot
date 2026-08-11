namespace QuickJot.ViewModels;

/// <summary>
/// Стек отмены на 20 операций, только в памяти — раздел 11. Между запусками не сохраняется:
/// обратные действия ссылаются на живые карточки, восстановить их из базы было бы отдельным продуктом.
/// </summary>
public sealed class UndoStack
{
    private const int Capacity = 20;

    private readonly record struct Entry(string Description, Action Undo, Action Redo);

    private readonly List<Entry> _undo = [];
    private readonly List<Entry> _redo = [];

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Push(string description, Action undo, Action redo)
    {
        _undo.Add(new Entry(description, undo, redo));
        if (_undo.Count > Capacity) _undo.RemoveAt(0); // самая старая операция выпадает

        _redo.Clear(); // новое действие обрывает ветку повтора
    }

    /// <returns>Описание отменённой операции или null, если отменять нечего.</returns>
    public string? Undo() => Move(_undo, _redo, entry => entry.Undo);

    public string? Redo() => Move(_redo, _undo, entry => entry.Redo);

    private static string? Move(List<Entry> from, List<Entry> to, Func<Entry, Action> action)
    {
        if (from.Count == 0) return null;

        var entry = from[^1];
        from.RemoveAt(from.Count - 1);
        action(entry)();
        to.Add(entry);

        return entry.Description;
    }
}
