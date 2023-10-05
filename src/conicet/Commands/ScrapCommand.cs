using System.ComponentModel;
using System.Text.Json;
using Devlooped.Web;
using MenosRelato.Commands.Cache;
using Polly;
using Spectre.Console;
using Spectre.Console.Cli;
using static Spectre.Console.AnsiConsole;

namespace MenosRelato.Commands;

[Description("Descarga articulos por area de conocimiento")]
public partial class ScrapCommand(ResiliencePipeline resilience, IHttpClientFactory factory) : AsyncCommand<ScrapCommand.Settings>
{
    // Add command settings allowing skipping pages
    public class Settings : CommandSettings
    {
        [CommandOption("-a|--all")]
        [Description("Refrescar todas las categorias")]
        public bool All { get; init; }

        [CommandOption("-p|--page")]
        [Description("# de pagina inicial")]
        public int Page { get; init; } = 0;
    }

    record ScrapArea(int Id, string Name, int Count) : Area(Id, Name);

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        using var http = factory.CreateClient();

        var doc = await resilience.ExecuteAsync(async x => HtmlDocument.Load(await http.GetStreamAsync("/subject/", x)));
        var areas = doc.CssSelectElements("#aspect_conicet_VerArea_list_nivel1 .ds-simple-list-item")
            .Select(x => x.CssSelectElements("span").Select(s => s.Value).ToArray())
            .Where(x => x.Length == 2)
            .Select(x => new ScrapArea(x[0].ToSubject(), x[0], int.Parse(x[1].Trim('[', ']'))))
            .ToList();

        if (!settings.All)
        {
            var prompt = new SelectionPrompt<string>().Title("Area a descargar:");
            foreach (var item in areas)
            {
                prompt.AddChoice($"{item.Name} ({item.Count})");
            }

            var selected = Prompt(prompt);
            areas.RemoveAll(x => selected.StartsWith(x.Name));
        }

        var cache = Path.Combine(Constants.DefaultCacheDir, "pubs");
        Directory.CreateDirectory(cache);

        foreach (var area in areas)
        {
            // Pages contain 20 links each.
            var current = settings.Page == 0 ? 0 : settings.Page * 20;
            var done = false;
            // Since we do a ++ up-front
            var page = settings.Page - 1;

            while (!done)
            {
                page++;
                done = await Status().StartAsync($"Procesando {area.Name} ({current} of {area.Count})...", async c =>
                {
                    doc = await resilience.ExecuteAsync(async c => HtmlDocument.Load(
                        await http.GetStreamAsync($"/subject/{area.Id}?pagina={page}", c)));

                    var links = doc.CssSelectElements(".ds-artifact-item .artifact-title a");
                    if (!links.Any())
                        return true;

                    foreach (var link in links)
                    {
                        current++;
                        c.Status = $"Procesando {area.Name} ({current} of {area.Count})...";
                        if (link.Attribute("href")?.Value is string articleUrl)
                        {
                            var articleFile = Path.Combine(cache, string.Join('-', articleUrl.Split('/').ToArray()[^2..]) + ".json");
                            if (File.Exists(articleFile) &&
                                JsonSerializer.Deserialize<Item>(File.ReadAllText(articleFile), ScrapGenerationContext.JsonOptions) is { } cached &&
                                // If we do have metadata or authors, we don't need full refresh
                                cached.Metadata?.Count > 0 &&
                                cached.Authors?.Count > 0)
                            {
                                if (cached.Area is null || cached.Title == null)
                                {
                                    cached = cached with { Area = area };

                                    if ((cached.Metadata.FirstOrDefault(x => x.Name == "DC.title")?.Content ??
                                        cached.Metadata.FirstOrDefault(x => x.Name == "citation_title")?.Content) is string t)
                                    {
                                        cached = cached with { Title = t };
                                    }

                                    File.WriteAllText(articleFile, JsonSerializer.Serialize(cached, ScrapGenerationContext.JsonOptions));
                                }

                                MarkupLine($"[green]✓[/] {articleUrl}");
                                continue;
                            }

                            await new FetchCommand(resilience, factory).ExecuteAsync(context, new FetchCommand.Settings
                            {
                                Area = area,
                                Uri = new(articleUrl, UriKind.RelativeOrAbsolute)
                            });
                        }
                    }

                    return false;
                });
            }
        }

        return 0;
    }
}
