using Dapper;
using MySqlConnector;

namespace TEİASRestfulApi;

public sealed class PlantNameDirectory
{
    private readonly Dictionary<int, string> _names;

    public PlantNameDirectory(IEnumerable<KeyValuePair<int, string>> names)
    {
        _names = names
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .GroupBy(kv => kv.Key)
            .ToDictionary(g => g.Key, g => g.First().Value.Trim());
    }

    public string NameOf(int plantId) =>
        _names.TryGetValue(plantId, out string? name) ? name : $"Santral {plantId}";

    public PlantRef RefOf(int plantId) => new(plantId, NameOf(plantId));

    public static PlantNameDirectory Load(string connectionString)
    {
        var names = new Dictionary<int, string>();
        using var connection = new MySqlConnection(connectionString);

        try
        {
            foreach (var row in connection.Query<(int Id, string Name)>(
                "SELECT TEIAS_PLANT_ID AS Id, SANTRAL_ADI AS Name FROM scada.TEIAS_PlantName"))
            {
                if (!string.IsNullOrWhiteSpace(row.Name))
                {
                    names[row.Id] = row.Name.Trim();
                }
            }
        }
        catch (MySqlException)
        {
            // Tablo henüz yoksa VAR adıyla devam.
        }

        foreach (var row in connection.Query<(int Id, string VarName)>(
            "SELECT TEIAS_PLANT_ID AS Id, VAR_NAME AS VarName FROM scada.TEIAS_Mapping"))
        {
            if (names.ContainsKey(row.Id) || string.IsNullOrWhiteSpace(row.VarName))
            {
                continue;
            }

            names[row.Id] = FromVarName(row.VarName);
        }

        return new PlantNameDirectory(names);
    }

    public static string FromVarName(string varName)
    {
        string body = varName;
        foreach (string suffix in new[] { ".ActivePower_PASIF", ".ActivePower", ".ActiveEnergy.Exported.Hourly" })
        {
            if (body.EndsWith(suffix, StringComparison.Ordinal))
            {
                body = body[..^suffix.Length];
                break;
            }
        }

        return body;
    }
}
