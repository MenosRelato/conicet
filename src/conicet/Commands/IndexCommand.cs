﻿using System.ComponentModel;
using System.Text.Json;
using Devlooped.Web;
using MenosRelato.Commands.Cache;
using Polly;
using Spectre.Console;
using Spectre.Console.Cli;
using static Spectre.Console.AnsiConsole;

namespace MenosRelato.Commands;

[Description("Generar índice por área de conocimiento")]
public partial class IndexCommand(ResiliencePipeline resilience, IHttpClientFactory factory) : AsyncCommand<IndexCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("-a|--all")]
        [Description("Indexar todas las categorías")]
        public bool All { get; init; }
    }

    record Article(string Title, string Url, int Year, string[] Tags)
    {
        public string Id => string.Join('-', Url.Split('/')[^2..]);
    }
    record Category(string Name, int Weight, long Count, string[] Articles, Category[] Categories);

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var areas = await factory.SelectAreasAsync(resilience, settings.All);
        var cache = Path.Combine(Constants.DefaultCacheDir, "pubs");
        Directory.CreateDirectory(cache);

        var dictionary = new Dictionary<Area, List<Article>>();

        foreach (var area in areas)
        {
            var count = 0;
            await Status().StartAsync($"Procesando {area.Name}...", async c =>
            {
                foreach (var file in Directory.EnumerateFiles(cache, "*.json"))
                {
                    if (JsonSerializer.Deserialize<Item>(File.ReadAllText(file), ScrapGenerationContext.JsonOptions) is { } item)
                    {
                        if (item.Area is null || item.Authors.Count == 0)
                        {
                            var title = item.Title ?? item.Metadata.FirstOrDefault(x => x.Name == "DC.title")?.Content ?? "";
                            title = new string(title.Take(80).ToArray()).Replace("[", "").Replace("]", "");

                            c.Status = $"Refrescando metadata de '{title}...'";
                            // Refresh item as it's missing area
                            if (await new FetchCommand(resilience, factory).ExecuteAsync(context, new FetchCommand.Settings { Uri = new(item.Handle) }) != 0)
                            {
                                WriteLine($"[red]x[/] {item.Handle} no tiene area");
                                continue;
                            }

                            item = JsonSerializer.Deserialize<Item>(File.ReadAllText(file), ScrapGenerationContext.JsonOptions);
                        }

                        if (item is null)
                            continue;

                        var articles = dictionary.GetValueOrDefault(area, new());
                        if (item.Metadata.FirstOrDefault(x => x.Name == "citation_keywords") is not { } citation)
                            continue;

                        count++;
                        c.Status = $"Procesando #{count} from {area.Name}...";

                        // Don't process older items
                        if (item.Date.Year < 2007) // || item.Date.Year >= DateTime.Now.Year)
                            continue;

                        var values = citation.Content.Split(';', StringSplitOptions.RemoveEmptyEntries)
                            .Select(x => x.Trim().ToLowerInvariant())
                            // Skip uri-like metadata and numbers
                            .Where(x => !Uri.TryCreate(x, UriKind.Absolute, out _))
                            .Where(x => !int.TryParse(x, out _))
                            .ToArray();

                        articles.Add(new(item.Title, item.Handle, item.Date.Year, values));
                    }
                }
            });
        }

        foreach (var entry in dictionary)
        {
            await Status().StartAsync($"Guardando articulos de {entry.Key.Name}...", async c =>
            {
                var fileName = $"{entry.Key.Id}-{entry.Key.Name.Sanitize().Replace(" ", "_")}";

                await File.WriteAllTextAsync(Path.Combine(Constants.DefaultCacheDir, $"{fileName}.json"),
                    JsonSerializer.Serialize(entry.Value, ScrapGenerationContext.JsonOptions));
            });
        }

        return 0;
    }
}
