using System.ComponentModel;
using System.Text.Json;
using System.Xml.Linq;
using Devlooped.Web;
using Polly;
using Spectre.Console;
using Spectre.Console.Cli;
using static Spectre.Console.AnsiConsole;

namespace MenosRelato.Commands;

[Description("Descarga articulos por area de conocimiento")]
public class Scrap(ResiliencePipeline resilience) : AsyncCommand
{
    record Meta(string Name, string Content, string? Lang);
    record Item(string Handle, DateOnly Date, List<Meta> Metadata);

    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };

        using var http = new HttpClient();
        http.BaseAddress = Constants.BaseAddress;
        http.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.UserAgent);

        var doc = await resilience.ExecuteAsync(async x => HtmlDocument.Load(await http.GetStreamAsync("/subject/")));
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

        var selected = Prompt(prompt);
        var area = areas.First(x => selected.StartsWith(x.Title));
        var subject = GetSubject(area.Title);
        var page = -1;

        var cache = Path.Combine("conicet", "pubs");
        var current = 0;
        var done = false;

        while (!done)
        {
            page++;
            done = await Status().StartAsync($"Procesando {area.Title} ({current} of {area.Count})...", async c =>
            {
                doc = await resilience.ExecuteAsync(async c => HtmlDocument.Load(
                    await http.GetStreamAsync($"/subject/{subject}?pagina={page}", c)));

                var links = doc.CssSelectElements(".ds-artifact-item .artifact-title a");
                if (!links.Any())
                    return true;

                foreach (var link in links)
                {
                    current++;
                    c.Status = $"Procesando {area.Title} ({current} of {area.Count})...";
                    if (link.Attribute("href")?.Value is string articleUrl)
                    {
                        var articleFile = Path.Combine(cache, string.Join('-', articleUrl.Split('/').ToArray()[^2..]) + ".json");
                        if (File.Exists(articleFile) &&
                            JsonSerializer.Deserialize<Publication>(File.ReadAllText(articleFile)) is { } cached)
                        {
                            MarkupLine($"[green]✓[/] {articleUrl}");
                            continue;
                        }

                        var article = await resilience.ExecuteAsync(async c => HtmlDocument.Load(
                            await http.GetStreamAsync(articleUrl, c)));

                        var meta = article.CssSelectElements("head meta")
                            .Select(x => new { Name = x.Attribute("name")?.Value, Content = x.Attribute("content")?.Value, Lang = x.Attribute(XNamespace.Xmlns + "lang")?.Value })
                            .Where(x => x is { } && !string.IsNullOrEmpty(x.Name) && !string.IsNullOrEmpty(x.Content))
                            .Select(x => new Meta(x.Name!, x.Content!, x.Lang))
                            .ToList();

                        var issued = meta.Where(x => x.Name == "DCTERMS.issued").Select(x => x.Content).FirstOrDefault() ?? 
                            meta.Where(x => x.Name == "DC.date").Select(x => x.Content).FirstOrDefault();

                        if (issued == null)
                        {
                            MarkupLine($"[red]x[/] {articleUrl} no tiene fecha como 'DCTERMS.issued' o 'DC.date'");
                            continue;
                        }

                        var date = DateOnly.TryParse(issued, out var d) ? d :
                            int.TryParse(issued, out var year) ? new DateOnly(year, 1, 1) : 
                            DateTime.TryParse(issued, out var dt) ? DateOnly.FromDateTime(dt) :
                            DateOnly.MinValue;

                        if (date == DateOnly.MinValue)
                        {
                            MarkupLine($"[red]x[/] {articleUrl} no tiene fecha valida como 'DCTERMS.issued' o 'DC.date': '{issued}'");
                            continue;
                        }

                        var handle = meta.FirstOrDefault(x => x.Name == "DC.identifier" && x.Content.StartsWith("http://hdl.handle.net/"));
                        if (handle == null)
                        {
                            WriteLine($"[red]x[/] {articleUrl} no tiene un identificador valido como 'DC.identifier'");
                            continue;
                        }

                        var pub = new Item(handle.Content, date, meta);
                        File.WriteAllText(articleFile, JsonSerializer.Serialize(pub, options));
                    }
                }

                return false;
            });
        }

        return 0;
    }

    // Hardcoded map from javascript functoin cargarArea(e) in https://ri.conicet.gov.ar/subject/  ¯\_(ツ)_/¯ 
    static int GetSubject(string area) => area switch
    {
        string s when s.Contains("NATURALES") => 1,
        string s when s.Contains("TECNOLOGÍAS") => 53,
        string s when s.Contains("MÉDICAS") => 108,
        string s when s.Contains("AGRÍCOLAS") => 174,
        string s when s.Contains("SOCIALES") => 192,
        string s when s.Contains("HUMANIDADES") => 225,
        _ => throw new NotImplementedException("Area inesperada: " + area),
    };
}
