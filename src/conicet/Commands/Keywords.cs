using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Devlooped.Web;
using MenosRelato.Commands.Cache;
using Polly;
using Spectre.Console;
using Spectre.Console.Cli;
using static Spectre.Console.AnsiConsole;


namespace MenosRelato.Commands;

[Description("Generar resumen de keywords por area de conocimiento")]
public partial class Keywords(ResiliencePipeline resilience, IHttpClientFactory factory) : AsyncCommand
{
    record Article(string Title, string Url, int Year, string[] Tags)
    {
        public string Id => string.Join('-', Url.Split('/')[^2..]); 
    }

    record Category(string Name, int Weight, long Count, string[] Articles, Category[] Categories);

    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        using var http = factory.CreateClient();

        var doc = await resilience.ExecuteAsync(async x => HtmlDocument.Load(await http.GetStreamAsync("/subject/", x)));
        var areas = doc.CssSelectElements("#aspect_conicet_VerArea_list_nivel1 .ds-simple-list-item")
            .Select(x => x.CssSelectElements("span").Select(s => s.Value).ToArray())
            .Where(x => x.Length == 2)
            .Select(x => new { Title = x[0], Count = int.Parse(x[1].Trim('[', ']')) })
            .ToList();

        var prompt = new SelectionPrompt<string>()
            .Title("Area a descargar:");

        foreach (var item in areas)
        {
            prompt.AddChoice($"{item.Title} ({item.Count})");
        }

        var response = Prompt(prompt);
        var selected = areas.First(x => response.StartsWith(x.Title));
        var area = Area.Create(selected.Title);

        var cache = Path.Combine(Constants.DefaultCacheDir, "pubs");
        Directory.CreateDirectory(cache);

        var articles = new List<Article>();
        var keywords = new Dictionary<string, long>();
        var timeline = new Dictionary<string, Dictionary<int, long>>();
        var count = 0;
        await Status().StartAsync($"Procesando {area.Name}...", async c =>
        {
            foreach (var file in Directory.EnumerateFiles(cache, "*.json"))
            {
                if (JsonSerializer.Deserialize<Item>(File.ReadAllText(file), ScrapGenerationContext.JsonOptions) is { } item)
                {
                    if (item.Area is null)
                    {
                        var title = item.Title ?? item.Metadata.FirstOrDefault(x => x.Name == "DC.title")?.Content ?? "";
                        title = new string(title.Take(80).ToArray()).Replace("[", "").Replace("]", "");

                        c.Status = $"Refrescando metadata de '{title}...'";
                        // Refresh item as it's missing area
                        if (await new Fetch(resilience, factory).ExecuteAsync(context, new Fetch.Settings { Uri = new(item.Handle) }) != 0)
                        {
                            WriteLine($"[red]x[/] {item.Handle} no tiene area");
                            continue;
                        }

                        item = JsonSerializer.Deserialize<Item>(File.ReadAllText(file), ScrapGenerationContext.JsonOptions);
                    }

                    if (item is null || item.Area.Id != area.Id)
                        continue;

                    if (item.Metadata.FirstOrDefault(x => x.Name == "citation_keywords") is not { } citation)
                        continue;

                    count++;
                    c.Status = $"Procesando #{count} from {area.Name}...";

                    // Don't process older items
                    if (item.Date.Year < 2007 || item.Date.Year >= DateTime.Now.Year)
                        continue;

                    var values = citation.Content.Split(';', StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim().ToLowerInvariant())
                        // Skip uri-like metadata and numbers
                        .Where(x => !Uri.TryCreate(x, UriKind.Absolute, out _))
                        .Where(x => !int.TryParse(x, out _))
                        .ToArray();

                    articles.Add(new(item.Title, item.Handle, item.Date.Year, values));

                    foreach (var value in values)
                    {
                        keywords[value] = keywords.GetValueOrDefault(value) + 1;
                        if (!timeline.TryGetValue(value, out var years))
                            timeline[value] = years = new Dictionary<int, long>();

                        years[item.Date.Year] = years.GetValueOrDefault(item.Date.Year) + 1;
                    }
                }
            }
        });

        //var map = articles.ToDictionary(articles => articles.Id, articles => articles.Tags);
        //var summary = new List<Category>();
        //foreach (var year in articles.GroupBy(articles => articles.Year).OrderBy(x => x.Key))
        //{

        //}

        var fileName = $"{area.Id}-{area.Name.Replace(" ", "_")}";

        Status().Start("Guardando articulos...", _ => File.WriteAllText(Path.Combine(Constants.DefaultCacheDir, $"{fileName}.json"),
            JsonSerializer.Serialize(articles, ScrapGenerationContext.JsonOptions)));

        Status().Start("Guardando temas...", _ => File.WriteAllText(Path.Combine(Constants.DefaultCacheDir, $"{area.Id}-{area.Name}.json"),
            JsonSerializer.Serialize(new
            {
                Area = area,
                Keywords = keywords.OrderByDescending(x => x.Value).Select(x => new { x.Key, x.Value }).ToList()
            }, ScrapGenerationContext.JsonOptions)));

        //Status().Start("Guardando linea de tiempo...", _ =>
        //{
        //    var rankings = Path.Combine(Constants.DefaultCacheDir, $"{area.Id}-{area.Name}.csv");
        //    var years = timeline.Values.SelectMany(x => x.Keys).Distinct().OrderBy(x => x).Select(x => x.ToString()).ToList();

        //    File.WriteAllLines(rankings, [string.Join(';', years.Prepend("indicator").Prepend("tema"))], Encoding.UTF8);
        //    foreach (var keyword in timeline.OrderBy(x => x.Value.Select(y => y.Value).Sum()))
        //    {
        //        File.AppendAllLines(rankings, [string.Join(';', new[] { keyword.Key, "tema" }
        //            .Concat(years.Select(x => keyword.Value.GetValueOrDefault(int.Parse(x)).ToString())))], Encoding.UTF8);
        //    }
        //});

        return 0;
    }
}
