﻿using Serilog.Configuration;
using Serilog.Events;
using System.Data.SQLite;

namespace Serilog.Sinks.SQLite
{
	public static class LoggerConfigurationSQLiteExtensions
	{
		public static LoggerConfiguration SQLite(
			this LoggerSinkConfiguration loggerConfiguration,
			string databasePath,
			TimeSpan? retentionPeriod = null,
			SQLiteJournalModeEnum journalMode = SQLiteJournalModeEnum.Wal,
			LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose)
		{
			if (loggerConfiguration == null) throw new ArgumentNullException(nameof(loggerConfiguration));

			var options = new SQLiteSinkOptions
			{
				DatabasePath = databasePath,
				JournalMode = journalMode
			};

			if (retentionPeriod.HasValue)
			{
				options.RetentionPeriod = retentionPeriod.Value;
			}

			return loggerConfiguration.Sink(new SQLiteSink(options), restrictedToMinimumLevel);
		}
	}
}
