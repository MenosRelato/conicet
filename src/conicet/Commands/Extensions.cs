using Devlooped.Web;
using MenosRelato.Commands.Cache;
using Polly;
using Spectre.Console;

namespace MenosRelato.Commands;

record AreaCount(int Id, string Name, int Count) : Area(Id, Name);

internal static class Conicet
{
    public static async Task<List<AreaCount>> SelectAreasAsync(this IHttpClientFactory factory, ResiliencePipeline resilience, bool all)
    {
        using var http = factory.CreateClient();

        var doc = await resilience.ExecuteAsync(async x => HtmlDocument.Load(await http.GetStreamAsync("/subject/", x)));
        var areas = doc.CssSelectElements("#aspect_conicet_VerArea_list_nivel1 .ds-simple-list-item")
            .Select(x => x.CssSelectElements("span").Select(s => s.Value).ToArray())
            .Where(x => x.Length == 2)
            .Select(x => new AreaCount(x[0].ToSubject(), x[0].Sanitize(), int.Parse(x[1].Trim('[', ']'))))
            .ToList();

        if (!all)
        {
            var prompt = new SelectionPrompt<string>().Title("Area a descargar:");
            foreach (var item in areas)
            {
                prompt.AddChoice($"{item.Name} ({item.Count})");
            }

            var selected = AnsiConsole.Prompt(prompt);
            areas.RemoveAll(x => !selected.StartsWith(x.Name));
        }

        return areas;
    }

    public static string Sanitize(this string area) => area.Replace('Á', 'A').Replace('É', 'E').Replace('Í', 'I');

    // Hardcoded map from javascript function cargarArea(e) in https://ri.conicet.gov.ar/subject/  ¯\_(ツ)_/¯ 
    public static int ToSubject(this string area) => area.Sanitize() switch
    {
        string s when s.Contains("NATURALES") => 1,
        string s when s.Contains("TECNOLOGIAS") => 53,
        string s when s.Contains("MEDICAS") => 108,
        string s when s.Contains("AGRICOLAS") => 174,
        string s when s.Contains("SOCIALES") => 192,
        string s when s.Contains("HUMANIDADES") => 225,
        _ => throw new NotImplementedException("Area inesperada: " + area.Sanitize()),
    };
}
