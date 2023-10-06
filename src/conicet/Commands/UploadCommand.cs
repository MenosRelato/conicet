using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using Humanizer;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Storage.DataMovement;
using Spectre.Console.Cli;
using static Spectre.Console.AnsiConsole;

namespace MenosRelato.Commands;

// El usuario debe estar logoneado con azcopy login tambien.
// Asume que 7zip esta instalado tambien
[Description("Sube los datos descargados a Azure Blob storage")]
public partial class UploadCommand : AsyncCommand<UploadCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [Description("The Azure Storage account connection string")]
        [CommandArgument(0, "<storage>")]
        public string Storage { get; set; } = default!;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var zip = new ProcessStartInfo("7z")
        {
            CreateNoWindow = true,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        if (Process.Start(zip) is not { } zproc ||
            zproc?.MainModule?.FileName is not string zpath ||
            !zproc.WaitForExit(1000) ||
            zproc.ExitCode != 0)
        {
            MarkupLine("[red]x[/] 7zip no esta instalado.");
            return -1;
        }

        // Prepare zipped indexes
        var zipdir = Path.Combine(Constants.DefaultCacheDir, "gzip");
        if (Directory.Exists(zipdir))
            Directory.Delete(zipdir, true);

        Directory.CreateDirectory(zipdir);
        // This will cause the current process to receive the output.
        //zip.UseShellExecute = false;
        zip.FileName = zpath;

        Status().Start($"Comprimiendo indices", ctx =>
        {
            foreach (var index in Directory.EnumerateFiles(Constants.DefaultCacheDir, "*.json"))
            {
                ctx.Status = "Comprimiendo " + Path.GetFileName(index);
                zip.Arguments = $"a -tgzip {Path.Combine(zipdir, Path.GetFileName(index))}.gz {index}";
                if (Process.Start(zip) is not { } ziproc ||
                    !ziproc.WaitForExit(-1) ||
                    ziproc.ExitCode != 0)
                {
                    MarkupLine($"[red]x[/] No se pudo comprimir {index}");
                }
            }
        });

        var account = CloudStorageAccount.Parse(settings.Storage);
        var blobClient = account.CreateCloudBlobClient();
        var container = blobClient.GetContainerReference("conicet");
        await container.CreateIfNotExistsAsync();
        await container.SetPermissionsAsync(new BlobContainerPermissions
        {
            PublicAccess = BlobContainerPublicAccessType.Blob
        });

        await Status().StartAsync($"Subiendo archivos", async ctx =>
        {
            var source = zipdir;
            var size = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Sum(x => new FileInfo(x).Length);
            var progress = new DirectoryTransferContext
            {
                ShouldOverwriteCallbackAsync = async (sourcePath, destinationPath) =>
                {
                    if (destinationPath is not ICloudBlob blob || 
                        sourcePath is not string sourceFile)
                        return true;

                    await blob.FetchAttributesAsync();

                    // Quickly check if the file is the same size and exit, since hashing is more expensive
                    if (new FileInfo(sourceFile).Length == blob.Properties.Length)
                        return false;

                    var targetHash = blob.Properties.ContentMD5;

                    // Calculate MD5 of sourceFile
                    using var stream = File.OpenRead(sourceFile);
                    var sourceHash = Convert.ToBase64String(await MD5.HashDataAsync(stream));

                    return sourceHash != targetHash;
                },
                ProgressHandler = new Progress<TransferStatus>(
                    (progress) => ctx.Status = $"Subiendo {source} ({progress.BytesTransferred.Bytes()} of {size.Bytes()})")
            };

            await TransferManager.UploadDirectoryAsync(source, container.GetDirectoryReference(new DirectoryInfo(source).Name),
                new UploadDirectoryOptions(), progress);

            source = Path.Combine(Constants.DefaultCacheDir, "pubs");
            size = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Sum(x => new FileInfo(x).Length);

            await TransferManager.UploadDirectoryAsync(source, container.GetDirectoryReference(new DirectoryInfo(source).Name),
                new UploadDirectoryOptions(), progress);
        });

        MarkupLine("[green]✓[/] Completado");

        return 0;
    }
}
