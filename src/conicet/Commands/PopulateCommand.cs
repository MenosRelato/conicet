using System.ComponentModel;
using System.Text.Json;
using Dapper;
using Devlooped.Web;
using MenosRelato.Commands.Cache;
using Microsoft.Data.Sqlite;
using Polly;
using Spectre.Console.Cli;
using Tomlyn.Model;
using static Spectre.Console.AnsiConsole;

namespace MenosRelato.Commands;

[Description("Popular una base de datos SQLite con los articulos descargados.")]
public partial class PopulateCommand(ResiliencePipeline resilience, IHttpClientFactory factory) : AsyncCommand<PopulateCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("-s|--skip")]
        [Description("# de items a saltear")]
        public int Skip { get; init; } = 0;

        [CommandOption("-t|--take")]
        [Description("# de items a procesar")]
        public int Take { get; init; } = int.MaxValue;
    }

    record Tag(int Id, string Name)
    {
        public Tag() : this(default, "") { }
    }
    record Meta(int Id, string Name, string Content, string? Lang)
    {
        public Meta() : this(default, "", "", "") { }
    }
    record Area(int Id, string Name);
    record Pub(string Id, string Title, string Url, string Date)
    {
        public Area? Area { get; init; }
        public List<Tag> Tags { get; init; } = new();
        public List<Meta> Meta { get; init; } = new();
    }

    static PopulateCommand()
    {
        SqlMapper.AddTypeHandler(new DateTimeOffsetHandler());
        SqlMapper.AddTypeHandler(new DateOnlyHandler());
        SqlMapper.AddTypeHandler(new GuidHandler());
        SqlMapper.AddTypeHandler(new TimeSpanHandler());
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var dbFile = Path.Combine(Constants.DefaultCacheDir, "conicet.db");
        if (!File.Exists(dbFile))
        {
            MarkupLine($"[yellow]![/] No se encontro la base de datos {dbFile}, inicializando.");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "empty.db"), dbFile);
        }

        await using var db = new SqliteConnection($"Data Source={dbFile}");
        using var http = factory.CreateClient();
        http.BaseAddress = new Uri("https://ri.conicet.gov.ar/");
        await db.OpenAsync();

        async ValueTask<int> InternAsync(string value)
        {
            if (db == null)
                throw new InvalidOperationException("Database not open");

            var id = (await db.QueryAsync<int>("select id from string where value = @value", new { value })).FirstOrDefault(-1);
            if (id != -1)
                return id;

            await db.ExecuteAsync("insert into string (value) values (@value)", new { value });
            return await db.ExecuteScalarAsync<int>("select last_insert_rowid()");
        }

        var count = 0;

        foreach (var file in Directory.EnumerateFiles(Path.Combine(Constants.DefaultCacheDir, "pubs"), "*.json"))
        {
            try
            {
                Markup($"[yellow]•[/] [link={file}]{Path.GetFileName(file)}[/] ");

                if (JsonSerializer.Deserialize<Item>(File.ReadAllText(file), ScrapGenerationContext.JsonOptions) is not { } item ||
                    // Don't process older items
                    item.Date.Year < 2007) // || item.Date.Year >= DateTime.Now.Year)
                    continue;

                if (item.Area is null || item.Authors.Count == 0)
                {
                    await Status().StartAsync($"Fetching {item.Handle}...", async _ => await new FetchCommand(resilience, factory).ExecuteAsync(context, new FetchCommand.Settings
                    {
                        Area = item.Area,
                        Uri = new(item.Handle)
                    }));

                    // Re-read the refreshed item.
                    item = JsonSerializer.Deserialize<Item>(File.ReadAllText(file), ScrapGenerationContext.JsonOptions);
                    if (item is null || item.Date.Year < 2007) // || item.Date.Year >= DateTime.Now.Year)
                        continue;
                }

                if (count < settings.Skip)
                {
                    count++;
                    continue;
                }

                var id = string.Join('-', item.Handle.Split('/')[^2..]);

                using var tx = await db.BeginTransactionAsync();

                var pub = (await db.QueryAsync<string>("select id from pub where id = @id", new { id })).FirstOrDefault();
                if (pub is not null)
                    continue;

                var area = (await db.QueryAsync<int>("select id from area where id = @id", new { id = item.Area.Id })).FirstOrDefault(-1);
                if (area == -1)
                {
                    await db.ExecuteAsync("insert into area (id, name) values (@id, @name)", new { id = item.Area.Id, name = item.Area.Name });
                    area = await db.ExecuteScalarAsync<int>("select last_insert_rowid()");
                }

                var tags = new List<int>();
                if (item.Metadata.FirstOrDefault(x => x.Name == "citation_keywords") is { } citation)
                {
                    var values = citation.Content.Split(';', StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim().ToLowerInvariant())
                        // Skip uri-like metadata and numbers
                        .Where(x => !Uri.TryCreate(x, UriKind.Absolute, out _))
                        .Where(x => !int.TryParse(x, out _))
                        .ToArray();

                    foreach (var value in values)
                        tags.Add(await InternAsync(value));
                }

                var metas = new List<int>();
                // persist all metadata in the meta table
                foreach (var meta in item.Metadata)
                {
                    var args = new
                    {
                        name = await InternAsync(meta.Name),
                        content = await InternAsync(meta.Content),
                        lang = meta.Lang == null ? (int?)null : await InternAsync(meta.Lang)
                    };

                    var metaId = meta.Lang is null ?
                        (await db.QueryAsync<int>("select id from meta where name = @name and content = @content", args)).FirstOrDefault(-1) :
                        (await db.QueryAsync<int>("select id from meta where name = @name and content = @content and lang = @lang", args)).FirstOrDefault(-1);

                    if (metaId == -1)
                    {
                        await db.ExecuteAsync("insert into meta (name, content, lang) values (@name, @content, @lang)", args);
                        metaId = await db.ExecuteScalarAsync<int>("select last_insert_rowid()");
                    }

                    metas.Add(metaId);
                }

                var authors = new List<string>();
                if (item.Authors is not null)
                {
                    foreach (var author in item.Authors)
                    {
                        if (db.Query<string>("select id from author where id = @id", new { id = author.Id }).FirstOrDefault() is null)
                        {
                            var profile = await resilience.ExecuteAsync(async c => HtmlDocument.Load(await http.GetStreamAsync("author/" + author.Id, c)));
                            string? title = null;
                            string? grade = null;
                            string? field = null;
                            string? specialty = null;

                            foreach (var row in profile.CssSelectElements("#aspect_conicet_VerAutor_table_datos tr"))
                            {
                                switch (row.Element("td")?.Value)
                                {
                                    case "Título":
                                        title = row.Elements("td").Last().Value;
                                        break;
                                    case "Grado":
                                        grade = row.Elements("td").Last().Value;
                                        break;
                                    case "Campo de aplicación":
                                        field = row.Elements("td").Last().Value;
                                        break;
                                    case "Especialidad":
                                        specialty = row.Elements("td").Last().Value;
                                        break;
                                    default:
                                        break;
                                }
                            }

                            await db.ExecuteAsync("insert into author (id, name, title, grade, field, specialty) values (@id, @name, @title, @grade, @field, @specialty)",
                                new { id = author.Id, name = author.Name, title, grade, field, specialty });
                        }

                        authors.Add(author.Id);
                    }
                }

                var collabs = new List<string>();
                if (item.Collaborators is not null)
                {
                    foreach (var author in item.Collaborators)
                    {
                        if (db.Query<string>("select id from author where id = @id", new { id = author.Id }).FirstOrDefault() is null)
                        {
                            var profile = await resilience.ExecuteAsync(async c => HtmlDocument.Load(await http.GetStreamAsync("author/" + author.Id, c)));
                            string? title = null;
                            string? grade = null;
                            string? field = null;
                            string? specialty = null;

                            foreach (var row in profile.CssSelectElements("#aspect_conicet_VerAutor_table_datos tr"))
                            {
                                switch (row.Element("td")?.Value)
                                {
                                    case "Título":
                                        title = row.Elements("td").Last().Value;
                                        break;
                                    case "Grado":
                                        grade = row.Elements("td").Last().Value;
                                        break;
                                    case "Campo de aplicación":
                                        field = row.Elements("td").Last().Value;
                                        break;
                                    case "Especialidad":
                                        specialty = row.Elements("td").Last().Value;
                                        break;
                                    default:
                                        break;
                                }
                            }

                            await db.ExecuteAsync("insert into author (id, name, title, grade, field, specialty) values (@id, @name, @title, @grade, @field, @specialty)",
                                new { id = author.Id, name = author.Name, title, grade, field, specialty });
                        }

                        collabs.Add(author.Id);
                    }
                }

                await db.ExecuteAsync("insert into pub (id, area, title, url, date, authors, collabs, tags, meta) values (@id, @area, @title, @url, @date, @authors, @collabs, @tags, @meta)",
                    new
                    {
                        id,
                        area,
                        title = item.Title,
                        url = item.Handle,
                        date = item.Date,
                        authors = string.Join(',', authors),
                        collabs = string.Join(',', collabs),
                        tags = string.Join(',', tags),
                        meta = string.Join(',', metas),
                    });

                await tx.CommitAsync();
                count++;

                if ((count - settings.Skip) == settings.Take)
                    break;
            }
            finally
            {
                MarkupLine("[green]✓[/]");
            }
        }

        return 0;
    }
}
