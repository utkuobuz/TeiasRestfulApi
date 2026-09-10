using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TEİASRestfulApi;

public sealed record TeiasRejectDetail(
    IReadOnlyList<int> CulpritIds,
    IReadOnlyList<string> Messages,
    IReadOnlyDictionary<int, decimal> Limits);

public static partial class TeiasErrorParser
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    [GeneratedRegex(@"Lisanssız santral ID bilgisi \((\d+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex InvalidIdRegex();

    [GeneratedRegex(@"Lisanssız santral ID:\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex OverLimitIdRegex();

    [GeneratedRegex(@"üst limitin \((\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex LimitRegex();

    public static TeiasRejectDetail Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new TeiasRejectDetail([], [], new Dictionary<int, decimal>());
        }

        List<string> messages = [];
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("mesaj", out JsonElement mesaj) && mesaj.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in mesaj.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        messages.Add(item.GetString() ?? "");
                    }
                }
            }
        }
        catch (JsonException)
        {
            messages.Add(body);
        }

        if (messages.Count == 0)
        {
            messages.Add(body);
        }

        var ids = new HashSet<int>();
        var limits = new Dictionary<int, decimal>();
        foreach (string message in messages)
        {
            foreach (Match match in InvalidIdRegex().Matches(message))
            {
                if (int.TryParse(match.Groups[1].Value, out int id))
                {
                    ids.Add(id);
                }
            }

            foreach (Match match in OverLimitIdRegex().Matches(message))
            {
                if (!int.TryParse(match.Groups[1].Value, out int id))
                {
                    continue;
                }

                ids.Add(id);
                Match limit = LimitRegex().Match(message);
                if (limit.Success && TryParseLimit(limit.Groups[1].Value, out decimal mw))
                {
                    limits[id] = mw;
                }
            }
        }

        return new TeiasRejectDetail(
            ids.OrderBy(x => x).ToList(),
            messages.Where(m => m.Length > 0).ToList(),
            limits);
    }

    private static bool TryParseLimit(string text, out decimal value)
    {
        return decimal.TryParse(text, NumberStyles.Number, Tr, out value)
            || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }
}
