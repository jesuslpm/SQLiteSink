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


		static void Main(string[] args)
		{
			ConfigureSerilog();
			using (var provider = CreateServiceProvider())
			{
				var listener = new ActivityListener
				{
					ShouldListenTo = source => true, // Escucha todas las actividades
					SampleUsingParentId = (ref ActivityCreationOptions<string> options) => ActivitySamplingResult.AllData,
					Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData
				};
				ActivitySource.AddActivityListener(listener);
				var logger = provider.GetRequiredService<ILogger<Program>>();
				using (var logScope = logger.BeginScope(new Dictionary<string, object> { ["TraceIdentifier"] = Guid.NewGuid()} ))
				using (var activity = MyActivitySource.StartActivity("Main"))
				{
					Activity.Current = activity;
					for (int i = 0; i < 10; i++)
					{
						logger.LogInformation("Hello, {Name}!. {Count}", "Serilog", i);
					}
				}
			}
			Log.CloseAndFlush();
		}
	}
}
