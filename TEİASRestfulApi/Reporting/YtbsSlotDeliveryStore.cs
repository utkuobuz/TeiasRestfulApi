using Dapper;
using MySqlConnector;

namespace TEİASRestfulApi;

public sealed class YtbsSlotDeliveryStore
{
    private const string CreatePlantSql = """
        CREATE TABLE IF NOT EXISTS scada.TEIAS_SlotPlant (
            CHANNEL VARCHAR(16) NOT NULL,
            SLOT_INSTANT DATETIME NOT NULL,
            PLANT_ID INT NOT NULL,
            STATUS VARCHAR(16) NOT NULL,
            UPDATED_AT DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            PRIMARY KEY (CHANNEL, SLOT_INSTANT, PLANT_ID)
        )
        """;

    private const string CreateAttemptSql = """
        CREATE TABLE IF NOT EXISTS scada.TEIAS_SlotAttempt (
            CHANNEL VARCHAR(16) NOT NULL,
            SLOT_INSTANT DATETIME NOT NULL,
            LAST_ATTEMPT DATETIME NOT NULL,
            ATTEMPT_COUNT INT NOT NULL,
            PRIMARY KEY (CHANNEL, SLOT_INSTANT)
        )
        """;

    public void EnsureSchema(string connectionString)
    {
        using var connection = new MySqlConnection(connectionString);
        connection.Open();
        connection.Execute(CreatePlantSql);
        connection.Execute(CreateAttemptSql);
    }

    public HashSet<(YtbsChannel Channel, DateTime Instant, int PlantId)> LoadResolved(
        string connectionString,
        DateTime fromInclusive,
        DateTime toInclusive)
    {
        using var connection = new MySqlConnection(connectionString);
        connection.Open();
        connection.Execute(CreatePlantSql);
        connection.Execute(CreateAttemptSql);

        var set = new HashSet<(YtbsChannel, DateTime, int)>();
        foreach (var row in connection.Query<(string Channel, DateTime Instant, int PlantId, string Status)>(
            """
            SELECT CHANNEL AS Channel, SLOT_INSTANT AS Instant, PLANT_ID AS PlantId, STATUS AS Status
            FROM scada.TEIAS_SlotPlant
            WHERE SLOT_INSTANT >= @From AND SLOT_INSTANT <= @To
            """,
            new { From = fromInclusive, To = toInclusive }))
        {
            if (!TryParseChannel(row.Channel, out YtbsChannel channel))
            {
                continue;
            }

            if (row.Status is not ("Ok" or "Skipped"))
            {
                continue;
            }

            set.Add((channel, row.Instant, row.PlantId));
        }

        return set;
    }

    public Dictionary<(YtbsChannel Channel, DateTime Instant), DateTime> LoadAttempts(
        string connectionString,
        DateTime fromInclusive,
        DateTime toInclusive)
    {
        using var connection = new MySqlConnection(connectionString);
        var map = new Dictionary<(YtbsChannel, DateTime), DateTime>();
        foreach (var row in connection.Query<(string Channel, DateTime Instant, DateTime LastAttempt)>(
            """
            SELECT CHANNEL AS Channel, SLOT_INSTANT AS Instant, LAST_ATTEMPT AS LastAttempt
            FROM scada.TEIAS_SlotAttempt
            WHERE SLOT_INSTANT >= @From AND SLOT_INSTANT <= @To
            """,
            new { From = fromInclusive, To = toInclusive }))
        {
            if (!TryParseChannel(row.Channel, out YtbsChannel channel))
            {
                continue;
            }

            map[(channel, row.Instant)] = row.LastAttempt;
        }

        return map;
    }

    public void Mark(string connectionString, YtbsChannel channel, DateTime instant, IEnumerable<int> plantIds, string status)
    {
        int[] ids = plantIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        using var connection = new MySqlConnection(connectionString);
        connection.Open();
        connection.Execute(CreatePlantSql);
        foreach (int plantId in ids)
        {
            connection.Execute(
                """
                INSERT INTO scada.TEIAS_SlotPlant (CHANNEL, SLOT_INSTANT, PLANT_ID, STATUS)
                VALUES (@Channel, @Instant, @PlantId, @Status)
                ON DUPLICATE KEY UPDATE STATUS = @Status
                """,
                new { Channel = channel.ToString(), Instant = instant, PlantId = plantId, Status = status });
        }
    }

    public void TouchAttempt(string connectionString, YtbsChannel channel, DateTime instant, DateTime now)
    {
        using var connection = new MySqlConnection(connectionString);
        connection.Open();
        connection.Execute(CreateAttemptSql);
        connection.Execute(
            """
            INSERT INTO scada.TEIAS_SlotAttempt (CHANNEL, SLOT_INSTANT, LAST_ATTEMPT, ATTEMPT_COUNT)
            VALUES (@Channel, @Instant, @Now, 1)
            ON DUPLICATE KEY UPDATE
                LAST_ATTEMPT = @Now,
                ATTEMPT_COUNT = ATTEMPT_COUNT + 1
            """,
            new { Channel = channel.ToString(), Instant = instant, Now = now });
    }

    private static bool TryParseChannel(string? text, out YtbsChannel channel)
    {
        return Enum.TryParse(text, ignoreCase: true, out channel)
            && channel is YtbsChannel.Anlik or YtbsChannel.Saatlik;
    }
}
