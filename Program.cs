using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebSearchMcp.Services;

namespace WebSearchMcp;

class Program
{
    static async Task Main(string[] args)
    {
        try
        {
            var host = CreateHost(args);
            
            using var scope = host.Services.CreateScope();
            var mcpServer = scope.ServiceProvider.GetRequiredService<McpServer>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            
            logger.LogInformation("Starting WebSearch MCP Server...");
            
            // Setup graceful shutdown
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                logger.LogInformation("Shutdown requested...");
            };
            
            await mcpServer.RunAsync(cts.Token);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex}");
            Environment.Exit(1);
        }
    }

    private static IHost CreateHost(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                config.AddEnvironmentVariables();
                config.AddCommandLine(args);
            })
            .ConfigureServices((context, services) =>
            {
                // Register HTTP client
                services.AddHttpClient<DuckDuckGoSearchService>();
                
                // Register services
                services.AddScoped<ISearchService, DuckDuckGoSearchService>();
                services.AddScoped<IDomainFilterService, DomainFilterService>();
                services.AddScoped<McpServer>();
            })
            .ConfigureLogging((context, logging) =>
            {
                logging.ClearProviders();
                logging.AddConsole(options =>
                {
                    // Force all console logging to stderr for MCP compatibility
                    options.LogToStandardErrorThreshold = LogLevel.Trace;
                });
                
                // Set minimum log level from configuration
                var logLevel = context.Configuration.GetValue<LogLevel>("Logging:LogLevel:Default", LogLevel.Information);
                logging.SetMinimumLevel(logLevel);
            })
            .Build();
    }
}