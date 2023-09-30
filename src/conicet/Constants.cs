using System.Net.Http.Headers;

namespace MenosRelato;

public static class Constants
{
    public static Uri BaseAddress { get; } = new("https://ri.conicet.gov.ar");
    public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/117.0.0.0 Safari/537.36 Edg/117.0.2045.41";
    public static string DefaultCacheDir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MenosRelato", "conicet");

    static Constants() => Directory.CreateDirectory(DefaultCacheDir);

    public static HttpClient CreateHttp()
    {
        var http = new HttpClient()
        {
            BaseAddress = BaseAddress
        };

        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        return http;
    }
}
