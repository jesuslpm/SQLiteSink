# SQLiteSink
Serilog sink that write logs to SQLite database.

```c#
Log.Logger = new LoggerConfiguration()
	.MinimumLevel.Debug()
	.Enrich.FromLogContext()
	.WriteTo.SQLite( 
		databasePath: path,
		journalMode: SQLiteJournalModeEnum.Wal,
		retentionPeriod: TimeSpan.FromDays(7))
	.CreateLogger();
```

## The Logs table

SQLiteSink writes logs to Logs table:

```SQL
CREATE TABLE Logs (
	Id INTEGER PRIMARY KEY,
	Timestamp datetime,
	SourceContext TEXT,
	Level TEXT,
	Message TEXT,
	TraceIdentifier TEXT,
	TraceId TEXT,
	SpanId TEXT,
	Properties TEXT,
	Exception TEXT
);
```


## Writing logs

SQLiteSink is efficient writing logs to database, It uses channels to write logs in the background. It writes all available logs from the channel in a single transaction.
