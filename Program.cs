using System.CommandLine;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Web;

var passwordOption = new Option<string>("--password", "-p")
{
    Description = "Icecast source password",
    Required = true
};

var urlOption = new Option<string>("--url", "-u")
{
    Description = "Icecast base URL",
    DefaultValueFactory = _ => "http://127.0.0.1:8000"
};

var mountOption = new Option<string>("--mount", "-m")
{
    Description = "Icecast mount point",
    DefaultValueFactory = _ => "/stream"
};

var rootCommand = new RootCommand("RDS metadata updater for Icecast");
rootCommand.Options.Add(passwordOption);
rootCommand.Options.Add(urlOption);
rootCommand.Options.Add(mountOption);

rootCommand.SetAction(async parseResult =>
{
    var password = parseResult.GetValue(passwordOption)!;
    var url = parseResult.GetValue(urlOption)!;
    var mount = parseResult.GetValue(mountOption)!;

    using var http = new HttpClient();
    var credentials = Convert.ToBase64String(
        System.Text.Encoding.UTF8.GetBytes($"source:{password}"));
    http.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Basic", credentials);

    string? lastTitle = null;
    string? stationName = null;

    await foreach (var line in ReadLinesAsync(Console.In))
    {
        if (string.IsNullOrWhiteSpace(line)) continue;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            var ps = root.TryGetProperty("ps", out var psProp)
                ? psProp.GetString()?.Trim() : null;
            var rt = root.TryGetProperty("radiotext", out var rtProp)
                ? rtProp.GetString()?.Trim() : null;

            // запам'ятовуємо першу PS як назву станції
            if (!string.IsNullOrEmpty(ps) && stationName == null)
                stationName = ps;

            // radiotext має пріоритет — це назва треку
            string? title = null;
            if (!string.IsNullOrEmpty(rt))
                title = stationName != null ? $"{stationName} - {rt}" : rt;
            else if (stationName != null && lastTitle == null)
                title = stationName; // перший раз — встановлюємо назву станції

            if (title == null || title == lastTitle) continue;
            lastTitle = title;

            var encoded = HttpUtility.UrlEncode(title);
            var requestUrl = $"{url}/admin/metadata?mount={mount}&mode=updinfo&song={encoded}";

            var response = await http.GetAsync(requestUrl);
            Console.Error.WriteLine($"RDS: {title} [{(int)response.StatusCode}]");
        }
        catch (JsonException) { }
    }
});

return rootCommand.Parse(args).Invoke();

static async IAsyncEnumerable<string> ReadLinesAsync(TextReader reader)
{
    while (await reader.ReadLineAsync() is { } line)
        yield return line;
}
