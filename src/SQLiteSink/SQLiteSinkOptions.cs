using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Serilog.Sinks.SQLite
{
	public class SQLiteSinkOptions
	{
		public string? DatabasePath { get; set; }
		public SQLiteJournalModeEnum JournalMode { get; set; } = SQLiteJournalModeEnum.Wal;
		public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);
	}
}
