namespace TEİASRestfulApi;

public sealed class YtbsIssueBuffer
{
    private readonly List<YtbsIssue> _items = [];
    private readonly object _gate = new();

    public void Add(YtbsIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        lock (_gate)
        {
            _items.Add(issue);
        }
    }

    /// <summary>
    /// Anlık: (hourStart, hourEnd] — dilim bitişi (08:00 = 07:45–08:00) o saatin özetine girer.
    /// Saatlik: slot == hourEnd (08:05 turunun etiketi 08:00).
    /// Login: [hourStart, hourEnd] (saat başı login hem önceki hem bu pencereye değebilir).
    /// </summary>
    public List<YtbsIssue> DequeueHour(DateTime hourStart)
    {
        DateTime end = hourStart.AddHours(1);
        lock (_gate)
        {
            var taken = _items.Where(i => Matches(i, hourStart, end)).ToList();
            foreach (var item in taken)
            {
                _items.Remove(item);
            }

            return taken;
        }
    }

    public static bool Matches(YtbsIssue issue, DateTime hourStart, DateTime hourEnd)
    {
        if (issue.Kind == YtbsIssueKind.LoginFailed)
        {
            return issue.Slot >= hourStart && issue.Slot <= hourEnd;
        }

        if (issue.Channel == YtbsChannel.Saatlik)
        {
            return issue.Slot == hourEnd;
        }

        return issue.Slot > hourStart && issue.Slot <= hourEnd;
    }
}
