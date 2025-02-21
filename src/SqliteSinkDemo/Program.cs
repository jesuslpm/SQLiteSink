using System.Reflection;
using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Sinks.SQLite;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Data.SQLite;

namespace SqliteSinkDemo
{
	internal class Program
	{

		private static IConfiguration? _configuration;

		private static readonly ActivitySource MyActivitySource = new("MyConsoleApp");

		public static IConfiguration Configuration
		{
			get
			{
				if (_configuration == null)
				{
					_configuration = CreateConfiguration();
				}
				return _configuration;
			}
		}
		static IConfiguration CreateConfiguration()
		{
			return new ConfigurationBuilder()
				.SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
				.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
			.Build();
		}

		private static ServiceProvider CreateServiceProvider()
		{
			var services = new ServiceCollection();
			services.AddSingleton(Configuration);
			services.AddLogging(builder => builder.AddSerilog(dispose: true));
			return services.BuildServiceProvider();
		}

		private static void ConfigureSerilog()
		{
			var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log.db");

			Log.Logger = new LoggerConfiguration()
				.MinimumLevel.Debug()
				.Enrich.FromLogContext()
				.WriteTo.SQLite( 
					databasePath: path,
					journalMode: SQLiteJournalModeEnum.Wal,
					retentionPeriod: TimeSpan.FromDays(7))
				.CreateLogger();
		}


		static async Task Main(string[] args)
		{
			ConfigureSerilog();
			using (var provider = CreateServiceProvider())
			{
				var logger = provider.GetRequiredService<ILogger<Program>>();
				var cts = new CancellationTokenSource();
				var writeLogTask = WriteLogs(logger, cts.Token);
				await Task.Delay(500);
				BackupDatabase();
				cts.Cancel();
				await writeLogTask.ConfigureAwait(false);
			}
			Log.CloseAndFlush();
		}

		static async Task WriteLogs( Microsoft.Extensions.Logging.ILogger logger, CancellationToken cancellationToken)
		{
			await Task.Yield();
			var listener = new ActivityListener
			{
				ShouldListenTo = source => true, // Escucha todas las actividades
				SampleUsingParentId = (ref ActivityCreationOptions<string> options) => ActivitySamplingResult.AllData,
				Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData
			};
			ActivitySource.AddActivityListener(listener);
			
			using (var logScope = logger.BeginScope(new Dictionary<string, object> { ["TraceIdentifier"] = Guid.NewGuid() }))
			using (var activity = MyActivitySource.StartActivity("Main"))
			{
				Activity.Current = activity;
				int i = 0;
				while (cancellationToken.IsCancellationRequested == false)
				{
					logger.LogInformation("Hello, {Name}!. {Count}", "Serilog", ++i);
				}
			}
		}

		static void BackupDatabase()
		{
			var watch = Stopwatch.StartNew();
			var databasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log.db");
			var backupPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log.db.bak");
			var dbBuilder = new SQLiteConnectionStringBuilder();
			dbBuilder.DataSource = databasePath;
			var bakBuilder = new SQLiteConnectionStringBuilder();
			bakBuilder.DataSource = backupPath;

			using (var cn = new SQLiteConnection(dbBuilder.ConnectionString))
			using (var bak = new SQLiteConnection(bakBuilder.ConnectionString))
			{
				cn.Open();
				bak.Open();
				cn.BackupDatabase(bak, "main", "main", -1, null, 10);
			}
			Console.WriteLine($"Backup completed in {watch.Elapsed}");
		}
	}
}
