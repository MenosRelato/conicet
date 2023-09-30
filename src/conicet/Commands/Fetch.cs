using System.ComponentModel;
using System.Text.Json;
using System.Xml.Linq;
using Devlooped.Web;
using MenosRelato.Commands.Cache;
using Polly;
using Spectre.Console;
using Spectre.Console.Cli;
using static Spectre.Console.AnsiConsole;

namespace MenosRelato.Commands;

[Description("Refrescar metadata de un articulo especifico")]
public class Fetch(ResiliencePipeline resilience, IHttpClientFactory factory) : AsyncCommand<Fetch.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandArgument(0, "[URL]")]
        [Description("URL del articulo a refrescar")]
        public Uri Uri { get; set; } = default!;

        public Area? Area { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        using var http = factory.CreateClient();

        var cache = Path.Combine(Constants.DefaultCacheDir, "pubs");
        Directory.CreateDirectory(cache);

        var articleUrl = settings.Uri.ToString();
        var articleFile = Path.Combine(cache, string.Join('-', articleUrl.Split('/').ToArray()[^2..]) + ".json");
        var article = await resilience.ExecuteAsync(async c => HtmlDocument.Load(await http.GetStreamAsync(settings.Uri, c)));

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
            return -1;
        }

        var date = DateOnly.TryParse(issued, out var d) ? d :
            int.TryParse(issued, out var year) ? new DateOnly(year, 1, 1) :
            DateTime.TryParse(issued, out var dt) ? DateOnly.FromDateTime(dt) :
            DateOnly.MinValue;

        if (date == DateOnly.MinValue)
        {
            MarkupLine($"[red]x[/] {articleUrl} no tiene fecha valida como 'DCTERMS.issued' o 'DC.date': '{issued}'");
            return -1;
        }

        var handle = meta.FirstOrDefault(x => x.Name == "DC.identifier" && x.Content.StartsWith("http://hdl.handle.net/"));
        if (handle == null)
        {
            WriteLine($"[red]x[/] {articleUrl} no tiene un identificador valido como 'DC.identifier'");
            return -1;
        }

        var title = meta.FirstOrDefault(x => x.Name == "DC.title")?.Content ??
            meta.FirstOrDefault(x => x.Name == "citation_title")?.Content;

        if (title == null)
        {
            WriteLine($"[red]x[/] {articleUrl} no tiene un titulo valido como 'DC.title' o 'citation_title'");
            return -1;
        }

        var area = settings.Area;

        if (area == null)
        {
            // Match area from DC.subject
            var doc = await resilience.ExecuteAsync(async x => HtmlDocument.Load(await http.GetStreamAsync("/subject/", x)));
            var areas = doc.CssSelectElements("#aspect_conicet_VerArea_list_nivel1 .ds-simple-list-item")
                .Select(x => x.CssSelectElements("span").Select(s => s.Value).ToArray())
                .Where(x => x.Length == 2)
                .Select(x => x[0])
                .ToHashSet();

            var subject = meta.FirstOrDefault(x => x.Name == "DC.subject" && areas.Contains(x.Content));
            if (subject == null)
            {
                WriteLine($"[red]x[/] {articleUrl} no tiene un area valida como 'DC.subject'");
                return -1;
            }

            area = Area.Create(subject.Content);
        }

        var pub = new Item(area, title, handle.Content, date, meta);
        File.WriteAllText(articleFile, JsonSerializer.Serialize(pub, ScrapGenerationContext.JsonOptions));

        return 0;
    }
}
