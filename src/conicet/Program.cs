using MenosRelato.Agent;
using MenosRelato.Commands;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly.Retry;
using Polly;
using Spectre.Console.Cli;
using static Spectre.Console.AnsiConsole;
using MenosRelato;

var config = new ConfigurationManager()
    .AddUserSecrets(ThisAssembly.Project.UserSecretsId)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection()
    .AddSingleton<IConfiguration>(config)
    .AddSingleton(_ => new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = int.MaxValue,
            BackoffType = DelayBackoffType.Linear,
            Delay = TimeSpan.FromSeconds(5),
            UseJitter = true,
            OnRetry = x =>
            {
                MarkupLine($"[red]x[/] Reintento #{x.AttemptNumber + 1}");
                return ValueTask.CompletedTask;
            },
        })
        //.AddTimeout(TimeSpan.FromSeconds(20))
        .Build())
    .AddSingleton<IAgentService>(sp => new CachingAgentService(
        new CloudAgentService(sp.GetRequiredService<IConfiguration>())))
    .AddHttpClient().ConfigureHttpClientDefaults(c => c.ConfigureHttpClient(
        http =>
        {
            http.BaseAddress = Constants.BaseAddress;
            http.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.UserAgent);
        }));

var app = new CommandApp(new TypeRegistrar(services));
// Register the app itself so commands can execute other commands
services.AddSingleton<ICommandApp>(app);

app.Configure(config =>
{
    config.SetApplicationName(ThisAssembly.Project.ToolCommandName);
    config.AddCommand<ScrapCommand>("scrap");
    config.AddCommand<FetchCommand>("fetch");
    config.AddCommand<IndexCommand>("index");
    config.AddCommand<SyncCommand>("sync");
    //config.AddCommand<Populate>("populate");

#if DEBUG
    config.PropagateExceptions();
#endif
});

#if DEBUG
if (args.Length == 0)
{
    var command = Prompt(
        new SelectionPrompt<string>()
            .Title("Command to run:")
            .AddChoices([
                "populate",
                "fetch",
                "scrap",
                "keywords",
                "help"
            ]));

    args = new[] { command };
}
#endif

return app.Run(args);