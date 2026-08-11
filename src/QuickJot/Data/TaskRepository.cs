using Dapper;
using Microsoft.Data.Sqlite;

namespace QuickJot.Data;

public sealed class TaskItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Notes { get; set; }
    public bool IsFlagged { get; set; }
    public bool IsExpanded { get; set; }
    public double SortOrder { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class TaskRepository(SqliteConnection db)
{
    /// <summary>Шаг между соседями при создании и перенумерации.</summary>
    public const double Step = 1000.0;

    /// <summary>Ниже этого разрыва вставка `(a+b)/2` перестаёт давать новое значение — раздел 9.</summary>
    public const double MinGap = 1e-6;

    /// <summary>Сколько раз пришлось перенумеровать список. Нужен проверке, в UI не используется.</summary>
    public int RenumberCount { get; private set; }

    private const string Columns =
        "id, title, notes, is_flagged, is_expanded, sort_order, completed_at, deleted_at, created_at, updated_at";

    public IReadOnlyList<TaskItem> Active() => db.Query<TaskItem>(
        $"SELECT {Columns} FROM tasks WHERE deleted_at IS NULL AND completed_at IS NULL ORDER BY sort_order").AsList();

    public IReadOnlyList<TaskItem> CompletedSince(DateTime sinceUtc) => db.Query<TaskItem>(
        $"SELECT {Columns} FROM tasks WHERE deleted_at IS NULL AND completed_at >= @since ORDER BY completed_at DESC",
        new { since = Db.Iso(sinceUtc) }).AsList();

    public TaskItem? Find(string id) => db.QuerySingleOrDefault<TaskItem>(
        $"SELECT {Columns} FROM tasks WHERE id = @id", new { id });

    /// <summary>Новая задача встаёт в начало списка — раздел 9.</summary>
    public TaskItem Create(string title, string? notes = null)
    {
        var now = DateTime.UtcNow;
        var first = db.ExecuteScalar<double?>(
            "SELECT MIN(sort_order) FROM tasks WHERE deleted_at IS NULL AND completed_at IS NULL");

        var item = new TaskItem
        {
            Id = Guid.NewGuid().ToString(),
            Title = title,
            Notes = notes,
            SortOrder = (first ?? 0) - Step,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Execute("""
            INSERT INTO tasks (id, title, notes, is_flagged, is_expanded, sort_order, created_at, updated_at)
            VALUES (@Id, @Title, @Notes, @IsFlagged, @IsExpanded, @SortOrder, @Now, @Now)
            """, new { item.Id, item.Title, item.Notes, item.IsFlagged, item.IsExpanded, item.SortOrder, Now = Db.Iso(now) });

        return item;
    }

    public void SetTitle(string id, string title) => Set("title", id, title);
    public void SetNotes(string id, string? notes) => Set("notes", id, notes);
    public void SetFlagged(string id, bool flagged) => Set("is_flagged", id, flagged);
    public void SetExpanded(string id, bool expanded) => Set("is_expanded", id, expanded);

    public void Complete(string id) => Set("completed_at", id, DateTime.UtcNow);
    public void Uncomplete(string id) => Set("completed_at", id, null);

    /// <summary>Мягкое удаление — раздел 9.</summary>
    public void Delete(string id) => Set("deleted_at", id, DateTime.UtcNow);
    public void Restore(string id) => Set("deleted_at", id, null);

    /// <summary>Физическая очистка. Вызывается полуночной проверкой, не при старте — раздел 9.</summary>
    public int PurgeDeletedOlderThan(TimeSpan age) => db.Execute(
        "DELETE FROM tasks WHERE deleted_at IS NOT NULL AND deleted_at < @cutoff",
        new { cutoff = Db.Iso(DateTime.UtcNow - age) });

    /// <summary>
    /// Переставить задачу между соседями. Считается `(a+b)/2`, но если разрыв уже меньше
    /// <see cref="MinGap"/>, точность double исчерпана и список сначала перенумеровывается —
    /// иначе соседние значения совпадут и порядок сломается молча.
    /// </summary>
    /// <returns>Новое значение sort_order — чтобы вызывающий не перечитывал строку ради него.</returns>
    public double MoveBetween(string id, string? aboveId, string? belowId)
    {
        var above = OrderOf(aboveId);
        var below = OrderOf(belowId);

        if (above is not null && below is not null && below.Value - above.Value < MinGap)
        {
            Renumber();
            above = OrderOf(aboveId);
            below = OrderOf(belowId);
        }

        double order = (above, below) switch
        {
            (null, null) => 0,
            (null, not null) => below!.Value - Step,
            (not null, null) => above!.Value + Step,
            _ => (above!.Value + below!.Value) / 2,
        };

        Set("sort_order", id, order);
        return order;
    }

    /// <summary>Вернуть прежнее значение при отмене перестановки — раздел 11.</summary>
    public void SetSortOrder(string id, double order) => Set("sort_order", id, order);

    /// <summary>Раздаёт всем неудалённым задачам значения с шагом <see cref="Step"/>, порядок сохраняется.</summary>
    private void Renumber()
    {
        var ids = db.Query<string>("SELECT id FROM tasks WHERE deleted_at IS NULL ORDER BY sort_order").AsList();
        using var tx = db.BeginTransaction();
        for (int i = 0; i < ids.Count; i++)
        {
            db.Execute("UPDATE tasks SET sort_order = @order WHERE id = @id",
                new { order = i * Step, id = ids[i] }, tx);
        }
        tx.Commit();
        RenumberCount++;
    }

    private double? OrderOf(string? id) => id is null
        ? null
        : db.ExecuteScalar<double?>("SELECT sort_order FROM tasks WHERE id = @id", new { id });

    // column приходит только из констант этого класса, значений извне тут нет.
    private void Set(string column, string id, object? value) => db.Execute(
        $"UPDATE tasks SET {column} = @value, updated_at = @now WHERE id = @id",
        new { value = value is DateTime stamp ? Db.Iso(stamp) : value, now = Db.Iso(DateTime.UtcNow), id });
}
