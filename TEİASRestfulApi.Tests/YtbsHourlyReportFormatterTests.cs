using TEİASRestfulApi;
using Xunit;

namespace TEİASRestfulApi.Tests;

public class YtbsHourlyReportFormatterTests
{
    [Fact]
    public void Format_returns_null_when_hour_is_clean()
    {
        Assert.Null(YtbsHourlyReportFormatter.Format(DateTime.Parse("2026-09-09 07:00"), []));
    }

    [Fact]
    public void Format_builds_agreed_subject_and_sections()
    {
        var issues = new List<YtbsIssue>
        {
            new()
            {
                Level = YtbsIssueLevel.Fail,
                Channel = YtbsChannel.Saatlik,
                Kind = YtbsIssueKind.PackageRejected,
                Slot = DateTime.Parse("2026-09-09 07:00"),
                LicenseNo = "ED-OSB/1325-7/966",
                PlantId = 11027,
                PlantName = "GÜR KALIP 3 GES",
                Reason = "Lisanssız santral ID bilgisi (11027) geçersizdir. Suçlu santral: 11027 (GÜR KALIP 3 GES).",
                RelatedPlants = [new PlantRef(11027, "GÜR KALIP 3 GES"), new PlantRef(11023, "GÜR KALIP 1 GES")]
            },
            new()
            {
                Level = YtbsIssueLevel.Fail,
                Channel = YtbsChannel.Anlik,
                Kind = YtbsIssueKind.OverLimitSkip,
                Slot = DateTime.Parse("2026-09-09 07:00"),
                PlantId = 11551,
                PlantName = "AĞAR OTO GES",
                Value = 0.655,
                Limit = 0.310m
            },
            new()
            {
                Level = YtbsIssueLevel.Fail,
                Channel = YtbsChannel.Anlik,
                Kind = YtbsIssueKind.OverLimitSkip,
                Slot = DateTime.Parse("2026-09-09 07:15"),
                PlantId = 11551,
                PlantName = "AĞAR OTO GES",
                Value = 0.640,
                Limit = 0.310m
            },
            new()
            {
                Level = YtbsIssueLevel.Warn,
                Channel = YtbsChannel.Anlik,
                Kind = YtbsIssueKind.NoSample,
                Slot = DateTime.Parse("2026-09-09 07:00"),
                PlantId = 26674,
                PlantName = "MPS METAL GES"
            },
            new()
            {
                Level = YtbsIssueLevel.Fail,
                Channel = YtbsChannel.Sistem,
                Kind = YtbsIssueKind.LoginFailed,
                Slot = DateTime.Parse("2026-09-09 07:15"),
                Reason = "TEİAŞ’a giriş yapılamadı; o turda hiç paket gitmedi."
            }
        };

        YtbsHourlyReport? report = YtbsHourlyReportFormatter.Format(DateTime.Parse("2026-09-09 07:00"), issues);

        Assert.NotNull(report);
        Assert.Equal(4, report.FailCount);
        Assert.Equal(1, report.WarnCount);
        Assert.Contains("YTBS uyarı —", report.Subject);
        Assert.Contains("07:00–08:00", report.Subject);
        Assert.Contains("4 hata, 1 uyarı", report.Subject);
        Assert.Contains("1) Saatlik (MWh) gönderim", report.Body);
        Assert.Contains("2) 15 dakikalık (MW) gönderim", report.Body);
        Assert.Contains("3) Login / bağlantı", report.Body);
        Assert.Contains("11027 GÜR KALIP 3 GES", report.Body);
        Assert.Contains("07:00  11551  AĞAR OTO GES", report.Body);
        Assert.Contains("07:15  11551  AĞAR OTO GES", report.Body);
        Assert.Contains("07:00  26674  MPS METAL GES", report.Body);
        Assert.Contains("fiziksel müdahale", report.Body);
    }

    [Fact]
    public void Buffer_takes_interval_end_slots_in_the_completed_hour()
    {
        var buffer = new YtbsIssueBuffer();
        buffer.Add(new YtbsIssue
        {
            Channel = YtbsChannel.Anlik,
            Kind = YtbsIssueKind.NoSample,
            Slot = DateTime.Parse("2026-09-09 07:45"),
            PlantId = 1
        });
        buffer.Add(new YtbsIssue
        {
            Channel = YtbsChannel.Anlik,
            Kind = YtbsIssueKind.NoSample,
            Slot = DateTime.Parse("2026-09-09 08:00"),
            PlantId = 2
        });
        buffer.Add(new YtbsIssue
        {
            Channel = YtbsChannel.Saatlik,
            Kind = YtbsIssueKind.OverLimitSkip,
            Slot = DateTime.Parse("2026-09-09 08:00"),
            PlantId = 3
        });

        List<YtbsIssue> taken = buffer.DequeueHour(DateTime.Parse("2026-09-09 07:00"));

        Assert.Equal(3, taken.Count);
        Assert.Contains(taken, i => i.PlantId == 1);
        Assert.Contains(taken, i => i.PlantId == 2);
        Assert.Contains(taken, i => i.PlantId == 3);
        List<YtbsIssue> next = buffer.DequeueHour(DateTime.Parse("2026-09-09 08:00"));
        Assert.Empty(next);
    }

    [Fact]
    public void Buffer_midnight_end_slots_belong_to_previous_clock_hour()
    {
        var buffer = new YtbsIssueBuffer();
        buffer.Add(new YtbsIssue
        {
            Channel = YtbsChannel.Anlik,
            Kind = YtbsIssueKind.NoSample,
            Slot = DateTime.Parse("2026-09-10 00:00"),
            PlantId = 1
        });
        buffer.Add(new YtbsIssue
        {
            Channel = YtbsChannel.Saatlik,
            Kind = YtbsIssueKind.NoSample,
            Slot = DateTime.Parse("2026-09-10 00:00"),
            PlantId = 2
        });

        List<YtbsIssue> taken = buffer.DequeueHour(DateTime.Parse("2026-09-09 23:00"));
        Assert.Equal(2, taken.Count);
        Assert.Empty(buffer.DequeueHour(DateTime.Parse("2026-09-10 00:00")));
    }

    [Fact]
    public void Format_shows_2400_for_midnight_interval_end()
    {
        var issues = new List<YtbsIssue>
        {
            new()
            {
                Level = YtbsIssueLevel.Warn,
                Channel = YtbsChannel.Anlik,
                Kind = YtbsIssueKind.NoSample,
                Slot = DateTime.Parse("2026-09-10 00:00"),
                PlantId = 26674,
                PlantName = "MPS METAL GES"
            }
        };

        YtbsHourlyReport? report = YtbsHourlyReportFormatter.Format(DateTime.Parse("2026-09-09 23:00"), issues);

        Assert.NotNull(report);
        Assert.Contains("24:00  26674  MPS METAL GES", report.Body);
    }
}
