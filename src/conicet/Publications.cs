using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using Devlooped.Web;
using MenosRelato.Agent;
using Polly.Retry;
using Polly;
using SharpYaml.Serialization;
using Spectre.Console;
using static Spectre.Console.AnsiConsole;

namespace MenosRelato;

partial class PublicationsAnalyzer(IAgentService agent)
{
    record Meta(string Name, string Content, string? Lang);
    record Publication(string Handle, DateOnly Date, List<Meta> Metadata);
    record Publications(string Topic, string Area, int Quantity);


    // Research areas according to https://www.conicet.gov.ar/wp-content/uploads/Nomina-de-RRHH-a-Dic-2022.xlsx
    static Dictionary<string, string> areas = new Dictionary<string, string>
    {
        { "KA", CultureInfo.CurrentCulture.TextInfo.ToTitleCase("CIENCIAS AGRARIAS, DE LA INGENIERÍA Y DE MATERIALES".ToLowerInvariant()) },
        { "KS", CultureInfo.CurrentCulture.TextInfo.ToTitleCase("CIENCIAS SOCIALES Y HUMANIDADES".ToLowerInvariant()) },
        { "KE", CultureInfo.CurrentCulture.TextInfo.ToTitleCase("CIENCIAS EXACTAS Y NATURALES".ToLowerInvariant()) },
        { "KB", CultureInfo.CurrentCulture.TextInfo.ToTitleCase("CIENCIAS BIOLÓGICAS Y DE LA SALUD".ToLowerInvariant()) },
        { "KT", CultureInfo.CurrentCulture.TextInfo.ToTitleCase("TECNOLOGÍA".ToLowerInvariant()) },
    };

    public async Task RunAsync()
    {
        // Create a instance of builder that exposes various extensions for adding resilience strategies
        var resilience = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = int.MaxValue,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(5),
                UseJitter = true,
                OnRetry = x =>
                {
                    MarkupLine($"[red]x[/] Retry #{x.AttemptNumber}");
                    return ValueTask.CompletedTask;
                },
            })
            .AddTimeout(TimeSpan.FromSeconds(10))
            .Build();

        var regex = new Regex("(?<topic>[^\\[]+)\\[(?<count>\\d+)\\]", RegexOptions.Compiled);
        var publications = new List<Publications>();

        //if (File.Exists("publicaciones.json") && JsonConvert.DeserializeObject<List<Publicaciones>>(File.ReadAllText("publicaciones.json")) is { } existing)
        //    publications = existing;
        var serializer = new Serializer();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        // cached article metadata
        Directory.CreateDirectory("articles");

        using var http = new HttpClient();
        http.BaseAddress = new Uri("https://ri.conicet.gov.ar");

        var docs = new ConcurrentDictionary<string, List<Publication>>();
        var offset = 0;
        while (true)
        {
            var doc = await resilience.ExecuteAsync(async c =>
            {
                var resp = await http.PostAsync($"/search-filter?field=subjectClassification&order=COUNT&offset={offset}&rpp=100", null, c);
                return HtmlDocument.Load(await resp.Content.ReadAsStreamAsync(c));
            });

            var table = doc.CssSelectElement(".ds-table");
            var rows = table.CssSelectElements("td");

            // reached the end of the list
            if (!rows.Any())
                break;

            foreach (var row in rows)
            {
                var text = row.Value;
                var match = regex.Match(text);
                if (!match.Success)
                {
                    MarkupLineInterpolated($"[red]x[/] {text}");
                    continue;
                }

                var topic = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(match.Groups["topic"].Value.Trim().ToLowerInvariant());
                var count = int.Parse(match.Groups["count"].Value);

                var prompt = new AgentPrompt(new AgentMessage(
                    """
                    Dadas las siguientes areas de investigacion:
                    """ +
                    string.Join(Environment.NewLine, areas.Select(x => $"* {x.Key} - {x.Value}")) +
                    $"""

                    A que area pertenecen publicaciones con la categoria "{topic}"? 
                    Responder solamente con los dos caracteres del area, por ejemplo: "KE".
                    NUNCA agregar NADA mas que los dos caracteres del area. Si no se puede 
                    catalogar, responder con "NA".
                    """))
                {
                    MaxTokens = 2,
                    Temperature = 0.5f,
                };

                var response = await agent.ProcessAsync(prompt);
                var code = response.Content;
                if (!areas.TryGetValue(code, out var area) && code != "NA")
                {
                    MarkupLineInterpolated($"[red]x[/] Area {code} not found");
                    continue;
                }
                else if (code == "NA")
                {
                    area = "INDETERMINADA";
                }

                publications.Add(new(topic, $"{code} - {area}", count));
                MarkupLine($"[green]✓[/] {code} - {topic} ({count})");


                var articles = row.CssSelectElement("a")?.Attribute("href")?.Value;
                if (articles is not null)
                {
                    await Status().StartAsync($"Procesando {topic} ({count})...", async c =>
                    {
                        var page = 0;
                        var current = 0;

                        while (true)
                        {
                            page++;
                            doc = await resilience.ExecuteAsync(async c =>
                            {
                                var html = await http.GetStringAsync($"{articles}&page={page}", c);
                                return HtmlDocument.Load(new StringReader(html));
                            });

                            var links = doc.CssSelectElements(".item-result-list .artifact-title a");
                            if (!links.Any())
                                break;

                            foreach (var link in links)
                            {
                                current++;
                                c.Status = $"Procesando {topic} ({current} of {count})...";
                                if (link.Attribute("href")?.Value is string articleUrl)
                                {
                                    var articleFile = Path.Combine("articles", string.Join('-', articleUrl.Split('/').ToArray()[^2..]) + ".json");
                                    if (File.Exists(articleFile) &&
                                        JsonSerializer.Deserialize<Publication>(File.ReadAllText(articleFile)) is { } cached)
                                    {
                                        MarkupLine($"[green]✓[/] {articleUrl} already processed");
                                        docs.GetOrAdd(code, _ => new List<Publication>())
                                            .Add(cached);
                                        continue;
                                    }

                                    // read article page url as an HtmlDocument and extract all the head/meta tags
                                    var article = await resilience.ExecuteAsync(async c 
                                        => HtmlDocument.Load(await http.GetStreamAsync(articleUrl, c)));

                                    var meta = article.CssSelectElements("head meta")
                                        .Select(x => new { Name = x.Attribute("name")?.Value, Content = x.Attribute("content")?.Value, Lang = x.Attribute(XNamespace.Xmlns + "lang")?.Value })
                                        .Where(x => x is { } && !string.IsNullOrEmpty(x.Name) && !string.IsNullOrEmpty(x.Content))
                                        .Select(x => new Meta(x.Name!, x.Content!, x.Lang))
                                        .ToList();

                                    var issued = meta.First(x => x.Name == "DCTERMS.issued").Content;
                                    var date = DateOnly.TryParse(issued, out var d) ? d :
                                        int.TryParse(issued, out var year) ? new DateOnly(year, 1, 1) :
                                        DateOnly.MinValue;

                                    if (date == DateOnly.MinValue)
                                    {
                                        MarkupLine($"[red]x[/] {articleUrl} has no valid date for DCTERMS.issued: '{issued}'");
                                        continue;
                                    }

                                    var handle = meta.FirstOrDefault(x => x.Name == "DC.identifier" && x.Content.StartsWith("http://hdl.handle.net/"));
                                    if (handle == null)
                                    {
                                        WriteLine($"[red]x[/] {articleUrl} has no DC.identifier");
                                        continue;
                                    }

                                    var pub = new Publication(handle.Content, date, meta);
                                    File.WriteAllText(articleFile, JsonSerializer.Serialize(pub, options));

                                    docs.GetOrAdd(code, _ => new List<Publication>())
                                        .Add(pub);

                                    //Write(new Panel(new JsonText(JsonSerializer.Serialize(meta, Json.Options)))
                                    //    .Header("Metadata")
                                    //    .Collapse()
                                    //    .RoundedBorder()
                                    //    .BorderColor(Color.Green));
                                }
                            }
                        }
                    });
                }
            }

            offset += 100;
        }

        foreach (var area in docs)
        {
            File.WriteAllText(area + ".yml", serializer.Serialize(area.Value));
        }
    }

    //[GeneratedRegex("(?<topic>[^\\[]+)\\[(?<count>\\d+)\\]", RegexOptions.Compiled)]
    //private static partial Regex TopicCountRegex();
}