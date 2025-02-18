
using Serilog;
using Serilog.Formatting.Compact;
using Serilog.Sinks.SQLite;
using Serilog.Sinks.SystemConsole.Themes;

namespace WebApiDemo
{
	public class Program
	{
		public static void Main(string[] args)
		{
			var builder = WebApplication.CreateBuilder(args);

			builder.Host.UseSerilog((context, services, configuration) =>
			{
				var logFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log");
				if (Directory.Exists(logFolderPath) == false) Directory.CreateDirectory(logFolderPath);
				var databasePath = Path.Combine(logFolderPath, "Log.db");
				configuration
					.MinimumLevel.Information()
					.ReadFrom.Configuration(context.Configuration)
					// .ReadFrom.Services(services)
					.Enrich.FromLogContext()
					.WriteTo.SQLite(databasePath);

				if (builder.Environment.IsDevelopment())
				{
					const string outputTemplate = "{Timestamp:HH:mm:ss.ff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";
					configuration.WriteTo.Console(outputTemplate: outputTemplate);
				}
			});

			builder.Services.AddControllers();
			// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
			builder.Services.AddOpenApi();

			var app = builder.Build();
			if (app.Environment.IsDevelopment())
			{
				app.MapOpenApi();
			}
			app.UseHttpsRedirection();
			app.UseAuthorization();
			app.MapControllers();

			app.Run();
		}
	}
}
