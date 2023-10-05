using System.ComponentModel;
using Spectre.Console.Cli;

namespace MenosRelato.Commands;

// Asume que AzCopy esta instalado con:
// winget install --id=Microsoft.Azure.AZCopy.10 -e
[Description("Sube los cambios a storage, usando azcopy.")]
public partial class Upload : Command
{
    public override int Execute(CommandContext context)
    {
        throw new NotImplementedException();
    }
}
