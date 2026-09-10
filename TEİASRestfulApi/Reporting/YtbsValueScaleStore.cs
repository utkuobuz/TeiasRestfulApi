using Dapper;
using MySqlConnector;

namespace TEİASRestfulApi;

public sealed class YtbsValueScaleStore
{
    private const string CreateSql = """
        CREATE TABLE IF NOT EXISTS scada.TEIAS_ValueScale (
            TEIAS_PLANT_ID INT NOT NULL,
            CHANNEL VARCHAR(16) NOT NULL,
            DIVISOR DECIMAL(16,0) NOT NULL,
            UPDATED_AT DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            PRIMARY KEY (TEIAS_PLANT_ID, CHANNEL)
        )
        """;

    public Dictionary<(int PlantId, YtbsChannel Channel), decimal> Load(string connectionString)
    {
        using var connection = new MySqlConnection(connectionString);
        connection.Open();
        connection.Execute(CreateSql);

        var map = new Dictionary<(int, YtbsChannel), decimal>();
        foreach (var row in connection.Query<(int PlantId, string Channel, decimal Divisor)>(
            """
            SELECT TEIAS_PLANT_ID AS PlantId, CHANNEL AS Channel, DIVISOR AS Divisor
            FROM scada.TEIAS_ValueScale
            """))
        {
            if (!TryParseChannel(row.Channel, out YtbsChannel channel) || row.Divisor <= 0)
            {
                continue;
            }

            map[(row.PlantId, channel)] = row.Divisor;
        }

        return map;
    }

    public void Upsert(string connectionString, int plantId, YtbsChannel channel, decimal divisor)
    {
        using var connection = new MySqlConnection(connectionString);
        connection.Open();
        connection.Execute(CreateSql);
        connection.Execute(
            """
            INSERT INTO scada.TEIAS_ValueScale (TEIAS_PLANT_ID, CHANNEL, DIVISOR)
            VALUES (@PlantId, @Channel, @Divisor)
            ON DUPLICATE KEY UPDATE DIVISOR = @Divisor
            """,
            new { PlantId = plantId, Channel = channel.ToString(), Divisor = divisor });
    }

    private static bool TryParseChannel(string? text, out YtbsChannel channel)
    {
        return Enum.TryParse(text, ignoreCase: true, out channel)
            && channel is YtbsChannel.Anlik or YtbsChannel.Saatlik;
    }
}
