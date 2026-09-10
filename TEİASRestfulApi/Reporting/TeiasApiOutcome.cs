using System.Text.Json;

namespace TEİASRestfulApi;

public readonly record struct TeiasApiOutcome(bool Ok, string? Body, IReadOnlyList<string> Messages);

public static class TeiasApiOutcomeParser
{
    /// <summary>
    /// HTTP 200 yetmez; gövdede <c>basarili</c> veya <c>gecerli</c> açıkça false ise paket reddidir.
    /// Boş / tanımsız gövde 200’de başarı sayılır.
    /// </summary>
    public static TeiasApiOutcome FromResponse(bool httpSuccess, string? body)
    {
        IReadOnlyList<string> messages = ReadMessages(body);
        if (!httpSuccess)
        {
            return new TeiasApiOutcome(false, body, messages);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return new TeiasApiOutcome(true, body, messages);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            JsonElement root = doc.RootElement;
            if (IsExplicitFalse(root, "basarili") || IsExplicitFalse(root, "gecerli"))
            {
                return new TeiasApiOutcome(false, body, messages);
            }
        }
        catch (JsonException)
        {
            // 200 + JSON değil: eski davranış (başarı).
        }

        return new TeiasApiOutcome(true, body, messages);
    }

    private static bool IsExplicitFalse(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement value)
            && value.ValueKind is JsonValueKind.False;
    }

    private static IReadOnlyList<string> ReadMessages(string? body)
    {
        return TeiasErrorParser.Parse(body).Messages;
    }
}
