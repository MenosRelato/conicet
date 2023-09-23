using MenosRelato.Agent;
using MenosRelato.Commands;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly.Retry;
using Polly;
using Spectre.Console.Cli;
using static Spectre.Console.AnsiConsole;

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
        .AddTimeout(TimeSpan.FromSeconds(10))
        .Build())
    .AddSingleton<IAgentService>(sp => new CachingAgentService(
        new CloudAgentService(sp.GetRequiredService<IConfiguration>())));

var app = new CommandApp(new TypeRegistrar(services));
// Register the app itself so commands can execute other commands
services.AddSingleton<ICommandApp>(app);

app.Configure(config =>
{
    config.SetApplicationName(ThisAssembly.Project.ToolCommandName);
    config.AddCommand<Scrap>("scrap");

#if DEBUG
    config.PropagateExceptions();
#endif
});

//var key = config["OpenAI:Key"];
//if (string.IsNullOrEmpty(key))
//{
//    MarkupLine("[red]x[/] Missing OpenAI:Key secret");
//    return -1;
//}

//await new PublicationsAnalyzer(agent).RunAsync();

return app.Run(args);