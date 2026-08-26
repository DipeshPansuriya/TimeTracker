using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TimeTracker.Mcp;
using TimeTracker.Storage;

// Local MCP server over stdio (brief §17). It talks only to the local encrypted database —
// it holds no HRMS credentials and has no network client, so an AI agent physically cannot
// reach HRMS through it. Submission stays behind a confirmation in the widget.
var builder = Host.CreateApplicationBuilder(args);

// stdio IS the protocol channel: anything written to stdout corrupts the JSON-RPC stream.
// Logging therefore goes to stderr and nowhere else.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton(_ => TimeTrackerDatabase.OpenDefault());
builder.Services.AddSingleton<TimesheetRepository>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<TimesheetTools>();

await builder.Build().RunAsync();
