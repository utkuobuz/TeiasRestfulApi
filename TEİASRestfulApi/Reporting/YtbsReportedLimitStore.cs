using Dapper;
using MySqlConnector;

namespace TEİASRestfulApi;

public sealed class YtbsReportedLimitStore
{
    private const string CreateSql = """
        CREATE TABLE IF NOT EXISTS scada.TEIAS_ReportedLimit (
            TEIAS_PLANT_ID INT NOT NULL,
            LIMIT_MW DECIMAL(16,6) NOT NULL,
            UPDATED_AT DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            PRIMARY KEY (TEIAS_PLANT_ID)
        )
        """;

    public Dictionary<int, decimal> Load(string connectionString)
    {
        using var connection = new MySqlConnection(connectionString);
        connection.Open();
        connection.Execute(CreateSql);

        var map = new Dictionary<int, decimal>();
        foreach (var row in connection.Query<(int PlantId, decimal LimitMw)>(
            """
            SELECT TEIAS_PLANT_ID AS PlantId, LIMIT_MW AS LimitMw
            FROM scada.TEIAS_ReportedLimit
            WHERE LIMIT_MW > 0
            """))
        {
            map[row.PlantId] = row.LimitMw;
        }

        return map;
    }

    public void Upsert(string connectionString, int plantId, decimal limitMw)
    {
        if (limitMw <= 0)
        {
            return;
        }

        using var connection = new MySqlConnection(connectionString);
        connection.Open();
        connection.Execute(CreateSql);
        connection.Execute(
            """
            INSERT INTO scada.TEIAS_ReportedLimit (TEIAS_PLANT_ID, LIMIT_MW)
            VALUES (@PlantId, @LimitMw)
            ON DUPLICATE KEY UPDATE LIMIT_MW = @LimitMw
            """,
            new { PlantId = plantId, LimitMw = limitMw });
    }
}
