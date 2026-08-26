using Microsoft.Data.Sqlite;

namespace TimeTracker.Storage;

/// <summary>
/// Owns the encrypted SQLite file: where it lives, how it is opened, and its schema.
/// </summary>
/// <remarks>
/// SQLCipher provides page-level encryption of the whole file, so the activity history is
/// not readable by opening the .db in any SQLite browser. The key never appears in
/// configuration — see <see cref="DatabaseKey"/>.
/// </remarks>
public sealed class TimeTrackerDatabase : IDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _connection;

    static TimeTrackerDatabase() => SQLitePCL.Batteries_V2.Init();

    public TimeTrackerDatabase(string databasePath, string keyHex)
    {
        DatabasePath = databasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Password = keyHex,
            Pooling = false          // we hold one connection ourselves; see Connect()
        }.ToString();
    }

    public string DatabasePath { get; }

    /// <summary>The default per-user location: %LOCALAPPDATA%\TimeTracker.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeTracker");

    public static TimeTrackerDatabase OpenDefault()
    {
        var dir = DefaultDirectory;
        var key = new DatabaseKey(Path.Combine(dir, "db.key")).GetOrCreate();
        var db = new TimeTrackerDatabase(Path.Combine(dir, "timetracker.db"), key);
        db.Migrate();
        return db;
    }

    /// <summary>
    /// The single long-lived connection. Callers must <b>not</b> dispose it.
    /// </summary>
    /// <remarks>
    /// One connection, opened once, rather than open-per-operation. SQLCipher derives the
    /// page key with PBKDF2 on every <c>Open()</c>, which is deliberately expensive — tens of
    /// milliseconds each. A widget that re-renders every 30 seconds and reads the day on each
    /// tick would spend most of its life deriving the same key. Measured: the test suite went
    /// from 37s to under 2s on this change alone.
    ///
    /// Single-user desktop app, so one connection is also the correct concurrency model;
    /// <see cref="TimesheetRepository"/> serialises access to it.
    /// </remarks>
    public SqliteConnection Connect()
    {
        if (_connection is { State: System.Data.ConnectionState.Open }) return _connection;

        _connection = new SqliteConnection(_connectionString);
        _connection.Open();

        using var pragma = _connection.CreateCommand();
        // WAL keeps the widget responsive while a write is in flight; FK enforcement makes
        // an orphaned break or location spell impossible rather than merely unlikely.
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();

        return _connection;
    }

    /// <summary>Creates the schema if absent. Safe to call on every start.</summary>
    public void Migrate()
    {
        using var command = Connect().CreateCommand();
        command.CommandText = Schema;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
    }

    // Times are stored as local wall-clock ISO strings without an offset, deliberately.
    // A timesheet is a claim about the employee's local day; round-tripping through UTC
    // introduces DST and travel bugs to solve a problem this application does not have.
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS config (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS work_days (
            date         TEXT PRIMARY KEY,
            started_at   TEXT NULL,
            completed_at TEXT NULL,
            note         TEXT NULL,
            sync_state   TEXT NOT NULL DEFAULT 'pending'
        );

        CREATE TABLE IF NOT EXISTS breaks (
            id       INTEGER PRIMARY KEY AUTOINCREMENT,
            date     TEXT NOT NULL REFERENCES work_days(date) ON DELETE CASCADE,
            start_at TEXT NOT NULL,
            end_at   TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS location_spells (
            id       INTEGER PRIMARY KEY AUTOINCREMENT,
            date     TEXT NOT NULL REFERENCES work_days(date) ON DELETE CASCADE,
            start_at TEXT NOT NULL,
            end_at   TEXT NULL,
            location TEXT NOT NULL
        );

        -- id is the activity identifier from the brief (ACT-YYYYMMDD-NNNNNN) and is the
        -- primary key on purpose: it makes a retried sync or a retried MCP write a no-op
        -- at the database level rather than a duplicate row.
        CREATE TABLE IF NOT EXISTS activities (
            id          TEXT PRIMARY KEY,
            date        TEXT NOT NULL,
            title       TEXT NOT NULL,
            customer    TEXT NULL,
            description TEXT NULL,
            notes       TEXT NULL,
            start_at    TEXT NOT NULL,
            end_at      TEXT NULL,
            status      TEXT NOT NULL,
            sync_state  TEXT NOT NULL DEFAULT 'pending'
        );

        CREATE INDEX IF NOT EXISTS ix_activities_date ON activities(date);
        CREATE INDEX IF NOT EXISTS ix_work_days_open  ON work_days(completed_at);
        """;
}
