using Serilog.Configuration;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Serilog.Sinks.SQLite
{
	public static class LoggerConfigurationSQLiteExtensions
	{
		public static LoggerConfiguration SQLite(
			this LoggerSinkConfiguration loggerConfiguration,
			string databasePath,
			TimeSpan? retentionPeriod = null,
			SQLiteJournalModeEnum journalMode = SQLiteJournalModeEnum.Wal)
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

			return loggerConfiguration.Sink(new SQLiteSink(options));
		}
	}
}
