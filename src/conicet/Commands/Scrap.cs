using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Devlooped.Web;
using Polly;
using Spectre.Console;
using Spectre.Console.Cli;
using static Spectre.Console.AnsiConsole;

namespace MenosRelato.Commands;

[Description("Descarga articulos por area de conocimiento")]
public partial class Scrap(ResiliencePipeline resilience) : AsyncCommand
{
    [JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(Item))]
    [JsonSerializable(typeof(Meta))]
    [JsonSerializable(typeof(Area))]
    [JsonSerializable(typeof(List<Meta>))]
    internal partial class ScrapGenerationContext : JsonSerializerContext
    {
    }

    public record Area(int Id, string Name);
    public record Meta(string Name, string Content, string? Lang);
    public record Item(string Title, string Handle, DateOnly Date, List<Meta> Metadata)
    {
        public Area? Area { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) 
        {
            TypeInfoResolver = ScrapGenerationContext.Default,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        using var http = new HttpClient();
        http.BaseAddress = Constants.BaseAddress;
        http.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.UserAgent);

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
                            JsonSerializer.Deserialize<Item>(File.ReadAllText(articleFile), options) is { } cached && 
                            cached.Metadata?.Count > 0)
                        {
                            if (cached.Area is null || cached.Title == null)
                            {
                                cached = cached with
                                {
                                    Area = new Area(subject, area.Title), 
                                };

                                if ((cached.Metadata.FirstOrDefault(x => x.Name == "DC.title")?.Content ??
                                    cached.Metadata.FirstOrDefault(x => x.Name == "citation_title")?.Content) is string t)
                                {
                                    cached = cached with
                                    {
                                        Title = t,
                                    };
                                }
                                
                                File.WriteAllText(articleFile, JsonSerializer.Serialize(cached, options));
                            }

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

                        var title = meta.FirstOrDefault(x => x.Name == "DC.title")?.Content ??
                            meta.FirstOrDefault(x => x.Name == "citation_title")?.Content;

                        if (title == null)
                        {
                            WriteLine($"[red]x[/] {articleUrl} no tiene un titulo valido como 'DC.title' o 'citation_title'");
                            continue;
                        }

                        var pub = new Item(title, handle.Content, date, meta)
                        {
                            Area = new(subject, area.Title)
                        };
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
