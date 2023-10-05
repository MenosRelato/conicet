using System.ComponentModel;
using Spectre.Console.Cli;

namespace MenosRelato.Commands;

[Description("Sincroniza todos los artículos de todas las categorías")]
public class SyncCommand(ICommandApp app) : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        if (await app.RunAsync(new[] { "scrap", "--all" }) is not 0)
            return -1;

        if (await app.RunAsync(new[] { "index", "--all" }) is not 0)
            return -1;

        return 0;
    }
}
