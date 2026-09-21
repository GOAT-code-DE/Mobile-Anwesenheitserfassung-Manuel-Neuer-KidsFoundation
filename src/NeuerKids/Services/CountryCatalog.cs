using System.Text.Json;

namespace NeuerKids.Services;

public static class CountryCatalog
{
    public static readonly Dictionary<string, string> All = Build();
    private static Dictionary<string, string> Build()
    {
        using var stream = typeof(CountryCatalog).Assembly.GetManifestResourceStream("NeuerKids.Resources.countries.de.json")
            ?? throw new InvalidOperationException("Länderkatalog fehlt.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
}
