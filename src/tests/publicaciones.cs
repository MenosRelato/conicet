using System.Collections.Generic;
using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.AI.OpenAI;
using Devlooped.Web;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Spectre.Console.Json;
using static Spectre.Console.AnsiConsole;

var config = new ConfigurationManager()
    .AddUserSecrets("365FCCC7-4AC1-4209-A0CC-F0DA6238AF4B")
    .Build();

var http = new HttpClient();
var offsets = new[] { 0, 100, 200, 300 };
var key = config["OpenAI:Key"];
if (string.IsNullOrEmpty(key))
{
    MarkupLine("[red]Missing OpenAI:Key secret[/]");
    return -1;
}

var client = new OpenAIClient(key);
var areas = new Dictionary<string, string>
{
    { "KA", CultureInfo.CurrentCulture.TextInfo.ToTitleCase("CIENCIAS AGRARIAS, DE LA INGENIERÍA Y DE MATERIALES".ToLowerInvariant()) },
    { "KS", CultureInfo.CurrentCulture.TextInfo.ToTitleCase("CIENCIAS SOCIALES Y HUMANIDADES".ToLowerInvariant()) },
    { "KE", CultureInfo.CurrentCulture.TextInfo.ToTitleCase("CIENCIAS EXACTAS Y NATURALES".ToLowerInvariant()) },
    { "KB", CultureInfo.CurrentCulture.TextInfo.ToTitleCase("CIENCIAS BIOLÓGICAS Y DE LA SALUD".ToLowerInvariant()) },
    { "KT", CultureInfo.CurrentCulture.TextInfo.ToTitleCase("TECNOLOGÍA".ToLowerInvariant()) },
};

var regex = new Regex("(?<topic>[^\\[]+)\\[(?<count>\\d+)\\]", RegexOptions.Compiled);
var publications = new List<Publicaciones>();

if (File.Exists("publicaciones.json") && JsonConvert.DeserializeObject<List<Publicaciones>>(File.ReadAllText("publicaciones.json")) is { } existing)
    publications = existing;

if (publications.Count == 0)
{

    foreach (var offset in offsets)
    {
        var resp = await http.PostAsync($"https://ri.conicet.gov.ar/search-filter?field=subjectClassification&order=COUNT&offset={offset}&rpp=100", null);
        var doc = HtmlDocument.Load(await resp.Content.ReadAsStreamAsync());
        var table = doc.CssSelectElement(".ds-table");
        var rows = table.CssSelectElements("td");

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
            //var topic = match.Groups["topic"].Value.Trim();
            var count = int.Parse(match.Groups["count"].Value);

            var options = new ChatCompletionsOptions
            {
                Messages =
            {
                new ChatMessage(ChatRole.User,
                """
                Dadas las siguientes areas de investigacion:
                """ +
                string.Join(Environment.NewLine, areas.Select(x => $"* {x.Key} - {x.Value}")) +
                $"""

                A que area pertenecen publicaciones con la categoria "{topic}"? 
                Responder solamente con los dos caracteres del area, por ejemplo: "KE".
                NUNCA agregar NADA mas que los dos caracteres del area. Si no se puede 
                catalogar, responder con "NA".
                """)
            },
                MaxTokens = 2,
                Temperature = 0.5f,
            };

            var completions = await client.GetChatCompletionsAsync("gpt-4", options);
            var code = completions.Value.Choices[0].Message.Content;
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

            //if (publications.Count == 10)
            //    break;
        }
    }
}

File.WriteAllText("publicaciones.json", JsonConvert.SerializeObject(publications, Formatting.Indented));
CsvSerializer.Serialize("publicaciones.csv", publications);

var groups = publications
    .GroupBy(x => x.Area)
    .Select(x => new { Area = x.Key, Count = x.Sum(y => y.Quantity) })
    .OrderByDescending(x => x.Count)
    .ToList();

MarkupLineInterpolated($"[green]Publicaciones[/]");
Write(new JsonText(JsonConvert.SerializeObject(groups, Formatting.Indented)));
File.WriteAllText("publicaciones-totals.json", JsonConvert.SerializeObject(groups, Formatting.Indented));

return 0;

record Publicaciones(string Topic, string Area, int Quantity);