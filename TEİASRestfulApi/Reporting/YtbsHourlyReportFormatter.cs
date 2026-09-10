using System.Globalization;
using System.Text;

namespace TEİASRestfulApi;

public sealed record YtbsHourlyReport(string Subject, string Body, int FailCount, int WarnCount);

public static class YtbsHourlyReportFormatter
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static YtbsHourlyReport? Format(DateTime periodStart, IReadOnlyList<YtbsIssue> issues)
    {
        if (issues.Count == 0)
        {
            return null;
        }

        DateTime periodEnd = periodStart.AddHours(1);
        int fails = issues.Count(i => i.Level == YtbsIssueLevel.Fail);
        int warns = issues.Count(i => i.Level == YtbsIssueLevel.Warn);

        string donem = $"{periodStart.ToString("d MMMM yyyy", Tr)} {periodStart:HH:mm}–{periodEnd:HH:mm}";
        string subject = $"YTBS uyarı — {donem} — {fails} hata, {warns} uyarı";

        var sb = new StringBuilder();
        sb.AppendLine("YTBS saatlik özet");
        sb.AppendLine($"Dönem: {periodStart:HH:mm}–{periodEnd:HH:mm}  |  {fails} hata, {warns} uyarı");
        sb.AppendLine();

        AppendChannel(sb, "1) Saatlik (MWh) gönderim", issues.Where(i => i.Channel == YtbsChannel.Saatlik).ToList());
        AppendChannel(sb, "2) 15 dakikalık (MW) gönderim", issues.Where(i => i.Channel == YtbsChannel.Anlik).ToList());
        AppendChannel(sb, "3) Login / bağlantı", issues.Where(i => i.Channel == YtbsChannel.Sistem).ToList());

        return new YtbsHourlyReport(subject, sb.ToString().TrimEnd(), fails, warns);
    }

    private static void AppendChannel(StringBuilder sb, string title, List<YtbsIssue> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        sb.AppendLine(title);

        foreach (var group in items.GroupBy(i => i.Kind).OrderBy(g => KindOrder(g.Key)))
        {
            sb.AppendLine($"   {KindHeading(group.Key)}");
            foreach (var issue in group.OrderBy(i => i.Slot).ThenBy(i => i.PlantId).ThenBy(i => i.LicenseNo))
            {
                AppendIssue(sb, issue);
            }

            sb.AppendLine();
        }
    }

    private static void AppendIssue(StringBuilder sb, YtbsIssue issue)
    {
        if (issue.Kind == YtbsIssueKind.PackageRejected)
        {
            sb.AppendLine($"   Lisans: {issue.LicenseNo}");
            sb.AppendLine($"   Neden: {issue.Reason}");
            if (issue.RelatedPlants.Count > 0)
            {
                sb.AppendLine($"   Bu yüzden gönderilmeyen ({issue.RelatedPlants.Count}):");
                foreach (var plant in issue.RelatedPlants)
                {
                    sb.AppendLine($"     {plant.Id} {plant.Name}");
                }
            }

            return;
        }

        if (issue.Kind == YtbsIssueKind.OverLimitSkip)
        {
            sb.AppendLine(
                $"   {IssueSaat(issue.Slot)}  {issue.PlantId}  {issue.PlantName}  ölçüm {Fmt(issue.Value)}  limit {Fmt(issue.Limit)}");
            return;
        }

        if (issue.Kind == YtbsIssueKind.NoSample)
        {
            sb.AppendLine($"   {IssueSaat(issue.Slot)}  {issue.PlantId}  {issue.PlantName}");
            return;
        }

        sb.AppendLine($"   {IssueSaat(issue.Slot)}  {issue.Reason}");
    }

    private static string KindHeading(YtbsIssueKind kind) => kind switch
    {
        YtbsIssueKind.PackageRejected => "Hata — lisans paketi reddedildi",
        YtbsIssueKind.OverLimitSkip => "Hata — limit aşıldı, santral paketten çıkarıldı (fiziksel müdahale)",
        YtbsIssueKind.NoSample => "Uyarı — SCADA örneği yok",
        YtbsIssueKind.LoginFailed => "Hata — TEİAŞ’a giriş yapılamadı",
        _ => kind.ToString()
    };

    private static int KindOrder(YtbsIssueKind kind) => kind switch
    {
        YtbsIssueKind.LoginFailed => 0,
        YtbsIssueKind.PackageRejected => 1,
        YtbsIssueKind.OverLimitSkip => 2,
        YtbsIssueKind.NoSample => 3,
        _ => 9
    };

    private static string IssueSaat(DateTime slot) => YtbsTimeSlots.ToIntervalEndSlot(slot).Saat;

    private static string Fmt(double? value) =>
        value is null ? "-" : value.Value.ToString("0.###", Tr);

    private static string Fmt(decimal? value) =>
        value is null ? "-" : value.Value.ToString("0.###", Tr);
}
