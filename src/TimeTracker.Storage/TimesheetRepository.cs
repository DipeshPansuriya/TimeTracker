using System.Globalization;
using Microsoft.Data.Sqlite;
using TimeTracker.Core;

namespace TimeTracker.Storage;

/// <summary>
/// Reads and writes days and activities. The only component that knows SQL — the widget, the
/// MCP server and the reports all go through here.
/// </summary>
public sealed class TimesheetRepository(TimeTrackerDatabase database)
{
    private const string TimeFormat = "yyyy-MM-ddTHH:mm:ss";
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// Serialises access to the single shared connection. The widget thread and the MCP
    /// server both reach this class, and one SQLite connection is not safe for concurrent use.
    /// </summary>
    private readonly Lock _gate = new();

    private T Query<T>(Func<SqliteConnection, T> read)
    {
        lock (_gate) return read(database.Connect());
    }

    /// <summary>Runs <paramref name="write"/> inside a transaction, under the gate.</summary>
    private void Transact(Action<SqliteConnection> write)
    {
        lock (_gate)
        {
            var connection = database.Connect();
            using var transaction = connection.BeginTransaction();
            write(connection);
            transaction.Commit();
        }
    }

    // ── days ──────────────────────────────────────────────────────────────────

    public WorkDay? LoadDay(DateOnly date) => Query(connection =>
    {
        DateTime? started, completed;
        string? note;

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT started_at, completed_at, note FROM work_days WHERE date = $d";
            command.Parameters.AddWithValue("$d", Fmt(date));

            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;

            started = ReadTime(reader, 0);
            completed = ReadTime(reader, 1);
            note = reader.IsDBNull(2) ? null : reader.GetString(2);
        }

        var result = started is { } s ? WorkDay.Started(s) : WorkDay.NotStarted(date);

        foreach (var (start, end) in ReadIntervals(connection, date))
            result = end is { } e ? result.WithBreak(start, e) : result.WithOpenBreak(start);

        foreach (var (start, _, location) in ReadSpells(connection, date))
            result = result.AtLocation(location, start);

        if (note is not null) result = result.WithNote(note);

        // Applied last: Completed() closes any still-open location spell, so it has to run
        // after the spells are restored or the day comes back with a spell that never ends.
        if (completed is { } c) result = result.Completed(c);

        return result;
    });

    public void SaveDay(WorkDay day)
    {
        ArgumentNullException.ThrowIfNull(day);

        Transact(connection =>
        {
            Execute(connection, """
                INSERT INTO work_days (date, started_at, completed_at, note, sync_state)
                VALUES ($d, $s, $c, $n, 'pending')
                ON CONFLICT(date) DO UPDATE SET
                    started_at = excluded.started_at,
                    completed_at = excluded.completed_at,
                    note = excluded.note,
                    sync_state = 'pending';
                """,
                ("$d", Fmt(day.Date)), ("$s", Fmt(day.StartedAt)),
                ("$c", Fmt(day.CompletedAt)), ("$n", (object?)day.Note ?? DBNull.Value));

            // Child rows are rewritten wholesale rather than diffed. The volume is a handful
            // of rows per day, and a replace cannot leave a stale interval behind the way a
            // partial update can — which would silently corrupt the day's arithmetic.
            Execute(connection, "DELETE FROM breaks WHERE date = $d", ("$d", Fmt(day.Date)));
            foreach (var b in day.Breaks)
                Execute(connection,
                    "INSERT INTO breaks (date, start_at, end_at) VALUES ($d, $s, $e)",
                    ("$d", Fmt(day.Date)), ("$s", Fmt(b.Start)), ("$e", Fmt(b.End)));

            Execute(connection, "DELETE FROM location_spells WHERE date = $d",
                ("$d", Fmt(day.Date)));
            foreach (var spell in day.Locations)
                Execute(connection, """
                    INSERT INTO location_spells (date, start_at, end_at, location)
                    VALUES ($d, $s, $e, $l)
                    """,
                    ("$d", Fmt(day.Date)), ("$s", Fmt(spell.Start)),
                    ("$e", Fmt(spell.End)), ("$l", spell.Location.ToString()));
        });
    }

    /// <summary>
    /// The most recent day the user started but never closed (the "you didn't close Tuesday"
    /// prompt). Returns null when nothing is outstanding.
    /// </summary>
    public DateOnly? FindUnclosedDayBefore(DateOnly today) => Query(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT date FROM work_days
            WHERE started_at IS NOT NULL AND completed_at IS NULL AND date < $today
            ORDER BY date DESC LIMIT 1
            """;
        command.Parameters.AddWithValue("$today", Fmt(today));

        return command.ExecuteScalar() is string s
            ? DateOnly.ParseExact(s, DateFormat, CultureInfo.InvariantCulture)
            : (DateOnly?)null;
    });

    public IReadOnlyList<DateOnly> DatesBetween(DateOnly from, DateOnly to) => Query(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT date FROM work_days WHERE date BETWEEN $f AND $t ORDER BY date";
        command.Parameters.AddWithValue("$f", Fmt(from));
        command.Parameters.AddWithValue("$t", Fmt(to));

        using var reader = command.ExecuteReader();
        var dates = new List<DateOnly>();
        while (reader.Read())
            dates.Add(DateOnly.ParseExact(reader.GetString(0), DateFormat, CultureInfo.InvariantCulture));
        return (IReadOnlyList<DateOnly>)dates;
    });

    // ── activities ────────────────────────────────────────────────────────────

    public ActivityLog LoadActivities(DateOnly date) => Query(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, title, customer, start_at, end_at, status, description, notes
            FROM activities WHERE date = $d ORDER BY start_at
            """;
        command.Parameters.AddWithValue("$d", Fmt(date));

        using var reader = command.ExecuteReader();
        var activities = new List<Activity>();
        while (reader.Read())
        {
            activities.Add(new Activity(
                Id: ActivityId.Parse(reader.GetString(0)),
                Title: reader.GetString(1),
                Customer: reader.IsDBNull(2) ? null : reader.GetString(2),
                Start: ReadTime(reader, 3)!.Value,
                End: ReadTime(reader, 4),
                Status: Enum.Parse<ActivityStatus>(reader.GetString(5)),
                Description: reader.IsDBNull(6) ? null : reader.GetString(6),
                Notes: reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return ActivityLog.Rehydrate(date, activities);
    });

    /// <summary>
    /// Persists the day's activities. Upserts on the activity id, so replaying the same write
    /// — a retried sync, a repeated MCP call — updates rather than duplicates (brief §20).
    /// </summary>
    public void SaveActivities(ActivityLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        Transact(connection =>
        {
            foreach (var a in log.Activities)
                Execute(connection, """
                    INSERT INTO activities
                        (id, date, title, customer, description, notes,
                         start_at, end_at, status, sync_state)
                    VALUES ($id, $d, $t, $c, $desc, $n, $s, $e, $st, 'pending')
                    ON CONFLICT(id) DO UPDATE SET
                        title = excluded.title, customer = excluded.customer,
                        description = excluded.description, notes = excluded.notes,
                        start_at = excluded.start_at, end_at = excluded.end_at,
                        status = excluded.status, sync_state = 'pending';
                    """,
                    ("$id", a.Id.Value), ("$d", Fmt(log.Date)), ("$t", a.Title),
                    ("$c", (object?)a.Customer ?? DBNull.Value),
                    ("$desc", (object?)a.Description ?? DBNull.Value),
                    ("$n", (object?)a.Notes ?? DBNull.Value),
                    ("$s", Fmt(a.Start)), ("$e", Fmt(a.End)), ("$st", a.Status.ToString()));
        });
    }

    // ── configuration (§1) ────────────────────────────────────────────────────

    public string? GetConfig(string key) => Query(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM config WHERE key = $k";
        command.Parameters.AddWithValue("$k", key);
        return command.ExecuteScalar() as string;
    });

    public void SetConfig(string key, string value) => Transact(connection =>
        Execute(connection, """
            INSERT INTO config (key, value) VALUES ($k, $v)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """, ("$k", key), ("$v", value)));

    // ── plumbing ──────────────────────────────────────────────────────────────

    private static void Execute(
        SqliteConnection connection, string sql, params (string Name, object? Value)[] args)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in args)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static List<(DateTime Start, DateTime? End)> ReadIntervals(
        SqliteConnection connection, DateOnly date)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT start_at, end_at FROM breaks WHERE date = $d ORDER BY start_at";
        command.Parameters.AddWithValue("$d", Fmt(date));

        using var reader = command.ExecuteReader();
        var rows = new List<(DateTime, DateTime?)>();
        while (reader.Read()) rows.Add((ReadTime(reader, 0)!.Value, ReadTime(reader, 1)));
        return rows;
    }

    private static List<(DateTime Start, DateTime? End, WorkLocation Location)> ReadSpells(
        SqliteConnection connection, DateOnly date)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT start_at, end_at, location FROM location_spells WHERE date = $d ORDER BY start_at";
        command.Parameters.AddWithValue("$d", Fmt(date));

        using var reader = command.ExecuteReader();
        var rows = new List<(DateTime, DateTime?, WorkLocation)>();
        while (reader.Read())
            rows.Add((ReadTime(reader, 0)!.Value, ReadTime(reader, 1),
                      Enum.Parse<WorkLocation>(reader.GetString(2))));
        return rows;
    }

    private static DateTime? ReadTime(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal)
            ? null
            : DateTime.ParseExact(reader.GetString(ordinal), TimeFormat, CultureInfo.InvariantCulture);

    private static object Fmt(DateTime? value)
        => value?.ToString(TimeFormat, CultureInfo.InvariantCulture) ?? (object)DBNull.Value;

    private static string Fmt(DateOnly value)
        => value.ToString(DateFormat, CultureInfo.InvariantCulture);
}
