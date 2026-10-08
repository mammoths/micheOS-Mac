// Timing and planning semantics adapted from current Windows Flow.
using System.Text.Json;
using Miche.Mac.Models;

namespace Miche.Mac.Services;

public sealed partial class FlowRepository
{
    // Only planning changes move the evening's estimate. Drafts, messages, clock
    // ticks and app restarts must not keep postponing quiet hours indefinitely.
    private static string WindDownInputs(TodayDocument d) => JsonSerializer.Serialize(new {
        StackItems=d.StackItems.Where(i=>!i.CouldDo), d.Commitments, d.Preferences.WindDownTime,
        d.Preferences.MorningStart, d.Preferences.TimeZoneId, d.DayRhythms
    });

    private static DateOnly WindDownDay(TodayDocument d, DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(d.Preferences.TimeZoneId));
        var date = DateOnly.FromDateTime(local.DateTime);
        return TimeOnly.FromDateTime(local.DateTime) < DailyRhythm.ForDate(d,date).MorningStart ? date.AddDays(-1) : date;
    }

    private static void SetStackWindDown(TodayDocument d, DateTimeOffset now)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(d.Preferences.TimeZoneId);
        var night = WindDownDay(d, now);
        var prefs=DailyRhythm.ForDate(d,night);
        // A bedtime after midnight belongs to the previous waking day.
        var date = prefs.WindDownTime < prefs.MorningStart ? night.AddDays(1) : night;
        var wall = date.ToDateTime(prefs.WindDownTime, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(wall)) wall = wall.AddMinutes(1);
        var baseline = new DateTimeOffset(wall, zone.GetUtcOffset(wall));
        var finish = OnDeckPlanner.Plan(d, now).Tonight.LastOrDefault()?.End;
        d.StackWindDownPlannedAt = now;
        d.StackWindDownAt = finish > baseline ? finish : baseline;
    }

    public void EnsureStackWindDown(DateTimeOffset now)
    {
        lock (_sync)
        {
            if (_document.StackWindDownAt != null && _document.StackWindDownPlannedAt is { } planned
                && WindDownDay(_document, planned) == WindDownDay(_document, now)) return;
            Commit(d => SetStackWindDown(d, now), contentChange: false, undoable: false, now);
        }
    }
}
