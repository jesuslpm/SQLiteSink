using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting.Json;
using System.Data.SQLite;
using System.Threading.Channels;

namespace Serilog.Sinks.SQLite
{
	internal class SQLiteSink : ILogEventSink,  IDisposable
	{
		private readonly SQLiteSinkOptions options;
		private readonly SQLiteConnection connection;
		private readonly Channel<LogEvent> queue = Channel.CreateBounded<LogEvent>(2048);
		private readonly Task processLogQueueTask;
		private readonly Task purgeLogsTask;
		private readonly CancellationTokenSource cts = new CancellationTokenSource();
		private readonly SQLiteCommand insertCommand;
		private SQLiteParameter? timestampParam;
		private SQLiteParameter? levelParam;
		private SQLiteParameter? messageParam;
		private SQLiteParameter? propsParam;
		private SQLiteParameter? sourceContextParam;
		private SQLiteParameter? traceIdentifierParam;
		private SQLiteParameter? traceIdParam;
		private SQLiteParameter? spanIdParam;
		private JsonValueFormatter jsonValueFormatter = new JsonValueFormatter();
		private SQLiteParameter? exceptionParam;
		private readonly ManualResetEventSlim processLogQueueEvent = new ManualResetEventSlim();
		private readonly ManualResetEventSlim purgeLogsEvent = new ManualResetEventSlim();
		private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1);

		internal SQLiteSink(SQLiteSinkOptions options)
		{
			this.options = options;
			connection = GetConnection();
			InitializeDatabase();
			insertCommand = CreateInsertCommand();
			this.processLogQueueTask = ProcessLogQueueAsync();
			this.purgeLogsTask = PurgeLogs();
		}
		public void Emit(LogEvent logEvent)
		{
			if (IsDisposed) return;
			this.queue.Writer.TryWrite(logEvent);
		}

		private SQLiteCommand CreateInsertCommand()
		{
			var command = connection.CreateCommand();
			command.CommandText = @"
INSERT INTO Logs (Timestamp, Level, Message, Properties, SourceContext, TraceIdentifier, Exception, TraceId, SpanId) 
VALUES (@ts, @level, @msg, @props, @sourceContext, @traceIdentifier, @exception, @traceId, @spanId);";

			this.timestampParam = command.CreateParameter();
			timestampParam.ParameterName = "@ts";
			timestampParam.DbType = System.Data.DbType.DateTime;
			command.Parameters.Add(timestampParam);

			this.levelParam = command.CreateParameter();
			levelParam.ParameterName = "@level";
			levelParam.DbType = System.Data.DbType.String;
			command.Parameters.Add(levelParam);

			this.messageParam = command.CreateParameter();
			messageParam.DbType = System.Data.DbType.String;
			messageParam.ParameterName = "@msg";
			messageParam.DbType = System.Data.DbType.String;
			command.Parameters.Add(messageParam);

			this.propsParam = command.CreateParameter();
			propsParam.ParameterName = "@props";
			propsParam.DbType = System.Data.DbType.String;
			command.Parameters.Add(propsParam);

			this.sourceContextParam = command.CreateParameter();
			sourceContextParam.ParameterName = "@sourceContext";
			sourceContextParam.DbType = System.Data.DbType.String;
			command.Parameters.Add(sourceContextParam);

			this.traceIdentifierParam = command.CreateParameter();
			traceIdentifierParam.ParameterName = "@traceIdentifier";
			traceIdentifierParam.DbType = System.Data.DbType.String;
			command.Parameters.Add(traceIdentifierParam);

			this.exceptionParam = command.CreateParameter();
			exceptionParam.ParameterName = "@exception";
			exceptionParam.DbType = System.Data.DbType.String;
			command.Parameters.Add(exceptionParam);

			this.traceIdParam = command.CreateParameter();
			traceIdParam.ParameterName = "@traceId";
			traceIdParam.DbType = System.Data.DbType.String;
			command.Parameters.Add(traceIdParam);

			this.spanIdParam = command.CreateParameter();
			spanIdParam.ParameterName = "@spanId";
			spanIdParam.DbType = System.Data.DbType.String;
			command.Parameters.Add(spanIdParam);

			return command;
		}


		private SQLiteConnection GetConnection()
		{
			var builder = new SQLiteConnectionStringBuilder();
			builder.DataSource = options.DatabasePath;
			builder.DateTimeFormat = SQLiteDateFormats.ISO8601;
			builder.DateTimeKind = DateTimeKind.Utc;
			builder.JournalMode = options.JournalMode;
			builder.FailIfMissing = false;
			builder.Pooling = false;
			var connectionString = builder.ConnectionString;
			var connection = new SQLiteConnection(connectionString);
			connection.Open();
			
			return connection;
		}

		private void InitializeDatabase()
		{
			using var command = connection.CreateCommand();
			command.CommandText = @"
CREATE TABLE IF NOT EXISTS Logs (
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
CREATE INDEX IF NOT EXISTS IX_Logs_Timestamp ON Logs(Timestamp);
CREATE INDEX IF NOT EXISTS IX_Logs_TraceIdentifier ON Logs(TraceIdentifier);";
			command.ExecuteNonQuery();
		}

		private static HashSet<string> ignoredProperties = new HashSet<string> { 
			"SourceContext", "TraceIdentifier", "SpanId", "TraceId" 
		};

		public string GetPropertiesJson(LogEvent logEvent)
		{
			using var output = new StringWriter();
			if (logEvent.Properties.Count != 0)
			{
				output.Write("{");
				char? separator = null;
				var props = logEvent.Properties.Where(p => ignoredProperties.Contains(p.Key) == false);
				foreach (KeyValuePair<string, LogEventPropertyValue> property in props)
				{
					if (separator.HasValue)
					{
						output.Write(separator.Value);
					}
					else
					{
						separator = ',';
					}
					JsonValueFormatter.WriteQuotedJsonString(property.Key, output);
					output.Write(':');
					jsonValueFormatter.Format(property.Value, output);
				}
				output.Write('}');
			}
			return output.ToString();
		}

		private async Task InsertEventAsync(LogEvent logEvent)
		{
			try 
			{ 
				if (logEvent.Properties.TryGetValue("SourceContext", out var sourceContext))
				{
					var scalarValue = sourceContext as ScalarValue;
					if (scalarValue == null)
					{
						this.sourceContextParam!.Value = sourceContext.ToString();
					}
					else
					{
						this.sourceContextParam!.Value = scalarValue.Value;
					}
				}
				else
				{
					this.sourceContextParam!.Value = DBNull.Value;
				}
				if (logEvent.Properties.TryGetValue("TraceIdentifier", out var traceIdentifier))
				{
					var scalarValue = traceIdentifier as ScalarValue;
					if (scalarValue == null)
					{
						this.traceIdentifierParam!.Value = traceIdentifier.ToString();
					}
					else
					{
						this.traceIdentifierParam!.Value = scalarValue.Value;
					}
				}
				else
				{
					this.traceIdentifierParam!.Value = DBNull.Value;
				}
				this.timestampParam!.Value = logEvent.Timestamp.UtcDateTime;
				this.levelParam!.Value = logEvent.Level.ToString();
				this.messageParam!.Value = logEvent.RenderMessage();
				var propertiesJson = GetPropertiesJson(logEvent);
				this.propsParam!.Value = (string.IsNullOrEmpty(propertiesJson) | propertiesJson == "{}") ? DBNull.Value : propertiesJson;
				this.traceIdParam!.Value = logEvent.TraceId.HasValue ? logEvent.TraceId.ToString() : DBNull.Value;
				this.spanIdParam!.Value = logEvent.SpanId.HasValue ? logEvent.SpanId.ToString() : DBNull.Value;
				this.exceptionParam!.Value = logEvent.Exception == null ? DBNull.Value : logEvent.Exception.ToString();
				await this.insertCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				SelfLog.WriteLine("Error while inserting log event: {0}", ex.ToString());
			}
		}

		private async Task ProcessLogQueueAsync()
		{
			try
			{
				while (await queue.Reader.WaitToReadAsync().ConfigureAwait(false))
				{
					await InsertLogRecordsInTransaction().ConfigureAwait(false);
				}
			}
			catch (OperationCanceledException)
			{
				// Ignore
			}
			catch (Exception ex)
			{
				SelfLog.WriteLine("Error while processing log queue: {0}", ex.ToString());
			}
			finally
			{
				processLogQueueEvent.Set();
			}
		}

		private async Task InsertLogRecordsInTransaction()
		{
			SQLiteTransaction? transaction = null;
			bool semaphoreAcquired = false;
			try
			{
				await semaphore.WaitAsync().ConfigureAwait(false);
				semaphoreAcquired = true;
				using (transaction = connection.BeginTransaction())
				{
					this.insertCommand.Transaction = transaction;
					while (queue.Reader.TryRead(out var logEvent))
					{
						await InsertEventAsync(logEvent).ConfigureAwait(false);
					}
					transaction.Commit();
				}
			}
			catch (Exception ex)
			{
				if (transaction != null)
				{
					try { transaction.Rollback(); } catch { }
				}
				SelfLog.WriteLine("Error while inserting log records in transaction: {0}", ex.ToString());
			}
			finally
			{
				if (semaphoreAcquired)
				{
					semaphore.Release();
				}
			}
		}

		private async Task PurgeLogs()
		{
			while (this.cts.IsCancellationRequested == false)
			{
				bool semaphoreAcquired = false;
				try
				{
					await semaphore.WaitAsync().ConfigureAwait(false);
					semaphoreAcquired = true;
					using var cmd = connection.CreateCommand();
					cmd.CommandText = "DELETE FROM Logs WHERE Timestamp < @ts";
					cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow - options.RetentionPeriod);
					await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					SelfLog.WriteLine("Error while purging logs: {0}", ex.ToString());
				}
				finally
				{
					if (semaphoreAcquired)
					{
						semaphore.Release();
					}
				}
				try
				{
					await Task.Delay(TimeSpan.FromMinutes(10), cts.Token).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					break;
				}
			}
			this.purgeLogsEvent.Set();
		}

		public bool IsDisposed { get; private set; }
		public void Dispose()
		{
			if (IsDisposed) return;
			IsDisposed = true;
			this.cts.Cancel();
			this.queue.Writer.TryComplete();
			processLogQueueEvent.Wait();
			purgeLogsEvent.Wait();
			if (options.JournalMode == SQLiteJournalModeEnum.Wal)
			{
				SetJournalMode(SQLiteJournalModeEnum.Delete);
			}
			TryDisposeConnection();
		}

		private void SetJournalMode(SQLiteJournalModeEnum journalMode)
		{
			try
			{
				using var cmd = connection.CreateCommand();
				cmd.CommandText = $"PRAGMA journal_mode = {journalMode.ToString()};";
				cmd.CommandTimeout = 1;
				cmd.ExecuteNonQuery();
			}
			catch (Exception ex)
			{
				SelfLog.WriteLine("Error while setting journal mode {0}: {1}", journalMode.ToString(), ex.ToString());
			}
		}

		private void TryDisposeConnection()
		{
			try
			{
				connection.Dispose();
			}
			catch (Exception ex)
			{
				SelfLog.WriteLine("Error while disposing SQLite connection: {0}", ex.ToString());
			}
		}
	}
}
