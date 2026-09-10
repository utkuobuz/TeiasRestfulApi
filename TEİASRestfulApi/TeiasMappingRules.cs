namespace TEİASRestfulApi;

/// <summary>
/// TEIAS_Mapping satırlarından hangilerinin canlı ActivePower olduğunu belirler.
/// PASİF eşanlamlılar gönderime ve kapsam sayımına girmez.
/// </summary>
public static class TeiasMappingRules
{
    /// <summary>
    /// MySQL LIKE: yalnızca *.ActivePower (sonda). *.ActivePower_PASIF eşleşmez.
    /// </summary>
    public const string LiveActivePowerSqlPredicate = "m.VAR_NAME LIKE '%.ActivePower'";

    /// <summary>
    /// Mapping ActivePower satırını Zenon saatlik enerji VAR'ına bağlar.
    /// </summary>
    public const string HourlyEnergyVarSql =
        "REPLACE(m.VAR_NAME, '.ActivePower', '.ActiveEnergy.Exported.Hourly')";

    public static bool IsLiveActivePowerVar(string? varName)
    {
        if (string.IsNullOrWhiteSpace(varName))
        {
            return false;
        }

        return varName.EndsWith(".ActivePower", StringComparison.Ordinal)
            && !varName.EndsWith("_PASIF", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPasifActivePowerVar(string? varName)
    {
        if (string.IsNullOrWhiteSpace(varName))
        {
            return false;
        }

        return varName.EndsWith(".ActivePower_PASIF", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsHourlyEnergyVar(string? varName)
    {
        return !string.IsNullOrWhiteSpace(varName)
            && varName.EndsWith(".ActiveEnergy.Exported.Hourly", StringComparison.Ordinal);
    }

    public static bool IsIgnoredYtbsVar(string? varName)
    {
        if (string.IsNullOrWhiteSpace(varName))
        {
            return false;
        }

        if (IsHourlyEnergyVar(varName))
        {
            return false;
        }

        return varName.EndsWith(".ActiveEnergy.Exported", StringComparison.Ordinal)
            || varName.EndsWith(".ReactivePower", StringComparison.Ordinal);
    }

    /// <summary>
    /// Mapping'deki *.ActivePower adından saatlik enerji VAR'ını üretir.
    /// Canlı güç değilse null (PASİF / enerji / reaktif karışmaz).
    /// </summary>
    public static string? ToHourlyEnergyVar(string? activePowerVar)
    {
        if (!IsLiveActivePowerVar(activePowerVar))
        {
            return null;
        }

        return string.Concat(
            activePowerVar.AsSpan(0, activePowerVar!.Length - ".ActivePower".Length),
            ".ActiveEnergy.Exported.Hourly");
    }

    /// <summary>
    /// 15 dk'da tekrar basılan saatlik MWh: her VAR için yalnızca en son örnek.
    /// Dört çeyrek toplanmaz.
    /// </summary>
    public static Dictionary<string, decimal> LatestValueByVar(
        IEnumerable<(string VarName, DateTime Timestamp, decimal Value)> rows)
    {
        return rows
            .GroupBy(r => r.VarName, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.Timestamp).First().Value,
                StringComparer.Ordinal);
    }
}

public sealed class MappingCoverageSnapshot
{
    public int ActivePlantCount { get; init; }
    public int ActiveVarCount { get; init; }
    public int PasifOnlyPlantCount { get; init; }
    public int AnlikMissingPlantCount { get; init; }
}
