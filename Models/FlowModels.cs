// Adapted from the verified October 6 Windows Flow data contract.
using System.Text.Json.Serialization;

namespace Miche.Mac.Models;

public sealed class TodayDocument
{
    public Dictionary<string,DayRhythm> DayRhythms { get; set; } = new();
    public List<OnDeckItem> StackItems { get; set; } = new();
    public string StackDraft { get; set; } = "";
    public string CouldDoDraft { get; set; } = "";
    public Dictionary<string, string> StackDayDrafts { get; set; } = new();
    public string Treat { get; set; } = "";
    public int Version { get; set; } = 1;
    public long Revision { get; set; }
    public long ContentRevision { get; set; }
    public TodayPreferences Preferences { get; set; } = new();
    public List<TodayDay> Days { get; set; } = new();
    public List<TodayMeal> Meals { get; set; } = new();
    public List<string> Pantry { get; set; } = new();
    public List<TodayCommitment> Commitments { get; set; } = new();
    public List<TodayMessage> Messages { get; set; } = new();
    public List<TodayProposal> Proposals { get; set; } = new();
    public List<Guid> Receipts { get; set; } = new();
    public List<TodaySnapshotReceipt> Snapshots { get; set; } = new();
    public string ReplyDraft { get; set; } = "";
    public DateTimeOffset? WindDownSnoozedUntil { get; set; }
    public DateTimeOffset? StackWindDownPlannedAt { get; set; }
    public DateTimeOffset? StackWindDownAt { get; set; }
}

public sealed class OnDeckItem
{
    // An explicit drag across a timed landmark; null preserves legacy flexible scheduling.
    public Guid? AfterTimedItemId { get; set; }
    public bool CouldDo { get; set; }
    public TimeOnly? FixedTime { get; set; }
    public int? BeforeBedtimeMinutes { get; set; }
    public bool FinishAtBedtime { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public int Minutes { get; set; } = 10;
    public bool Movement { get; set; }
    public DateOnly? MovementDate { get; set; }
    public DateOnly? NotBefore { get; set; }
    public TimeOnly? PlannedTime { get; set; }
    public string? PlannedBand { get; set; }
    public bool MustToday { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public double ElapsedSeconds { get; set; }
    public DateTimeOffset? ReviewAt { get; set; }
    public List<OnDeckTimeBlock> TimeBlocks { get; set; } = new();
    public double Elapsed(DateTimeOffset now) => ElapsedSeconds + (StartedAt is { } start ? Math.Max(0, (now - start).TotalSeconds) : 0);
}

public sealed record OnDeckTimeBlock(DateTimeOffset End, double Seconds, bool EndedEarly);

public sealed class DayRhythm
{
    public TimeOnly? Wake { get; set; }
    public TimeOnly? Bed { get; set; }
}

public sealed class TodayPreferences
{
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
    public TimeOnly NoteTime { get; set; } = new(19, 0);
    public TimeOnly WindDownTime { get; set; } = new(23, 0);
    public TimeOnly MorningStart { get; set; } = new(7, 0);
    public TimeOnly WorkStart { get; set; } = new(12, 0);
    public TimeOnly EveningStart { get; set; } = new(17, 0);
    public bool WindDownEnabled { get; set; } = true;
    public bool StartOnToday { get; set; }
    public decimal? ProteinTargetGrams { get; set; }
    public decimal? FiberTargetGrams { get; set; }
    public List<string> WindDownActivities { get; set; } = new()
    { "Refill your water", "Wash your face and do skincare", "Tidy one small space", "Load the dishwasher", "Try a skincare mask", "Read something gentle" };
    public List<Guid> QuietMicheIds { get; set; } = new();
    public string AudiobookTitle { get; set; } = "";
    public Dictionary<DayOfWeek, string> WeeklyMovement { get; set; } = new();
}

public sealed class TodayDay
{
    public DateOnly Date { get; set; }
    public string State { get; set; } = "prepared";
    public string Origin { get; set; } = "local";
    public DateTimeOffset PreparedAt { get; set; } = DateTimeOffset.UtcNow;
    public string WindDownSuggestion { get; set; } = "";
    public List<TodayChapter> Chapters { get; set; } = new();
    public List<TodayPlannedMeal> PlannedMeals { get; set; } = new();
    public List<TodayMealLog> MealLogs { get; set; } = new();
}

public sealed class TodayChapter
{
    public string Key { get; set; } = "morning";
    public string Title { get; set; } = "Body";
    public string NextAction { get; set; } = "";
    public string GentlerAction { get; set; } = "";
    public string State { get; set; } = "ready";
    public string Note { get; set; } = "";
    public int? Minutes { get; set; }
}

public sealed class TodayMeal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    // Nutrients are per one portion; null means unknown, never zero.
    public decimal? ProteinGrams { get; set; }
    public decimal? FiberGrams { get; set; }
    public bool Estimated { get; set; } = true;
}

public sealed class TodayPlannedMeal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? MealId { get; set; }
    public string Name { get; set; } = "";
    public decimal Portions { get; set; } = 1;
    public decimal? ProteinGrams { get; set; }
    public decimal? FiberGrams { get; set; }
    public bool Estimated { get; set; } = true;
}

public sealed class TodayMealLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? MealId { get; set; }
    public string Name { get; set; } = "";
    public decimal Portions { get; set; } = 1;
    public decimal? ProteinGrams { get; set; }
    public decimal? FiberGrams { get; set; }
    public bool Estimated { get; set; } = true;
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TodayCommitment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
    public string Location { get; set; } = "";
    public int? TravelMinutes { get; set; }
}

public sealed class TodayMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Role { get; set; } = "user";
    public string Text { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? ReplyTo { get; set; }
    public List<string> Choices { get; set; } = new();
    public DateTimeOffset? SourceSnapshotAt { get; set; }
}

public sealed class TodayProposal
{
    [JsonRequired]
    public int Version { get; set; } = 1;
    [JsonRequired]
    public Guid Id { get; set; } = Guid.NewGuid();
    [JsonRequired]
    public Guid SnapshotId { get; set; }
    [JsonRequired]
    public long BaseRevision { get; set; }
    [JsonRequired]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Message { get; set; } = "";
    public List<string> Choices { get; set; } = new();
    public TodayDay? Plan { get; set; }
    public string Status { get; set; } = "pending";
    public string StatusReason { get; set; } = "";
}

public sealed class TodaySnapshotReceipt
{
    public Guid Id { get; set; }
    public long ContentRevision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateOnly TargetDate { get; set; }
}

public sealed class TodayContextSnapshot
{
    public List<OnDeckItem> StackItems { get; set; } = new();
    public string Treat { get; set; } = "";
    public int Version { get; set; } = 1;
    public Guid SnapshotId { get; set; } = Guid.NewGuid();
    public long ContentRevision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string TimeZoneId { get; set; } = "";
    public DateOnly TodayDate { get; set; }
    public DateOnly TargetDate { get; set; }
    public TodayPreferences Preferences { get; set; } = new();
    public List<TodayDay> Days { get; set; } = new();
    public List<TodayMeal> Meals { get; set; } = new();
    public List<string> Pantry { get; set; } = new();
    public List<TodayCommitment> Commitments { get; set; } = new();
    public List<TodayMessage> Messages { get; set; } = new();
}

public sealed class TodayScheduleState
{
    public DateOnly LocalDate { get; init; }
    public DateTimeOffset LocalNow { get; init; }
    public string ChapterKey { get; init; } = "morning";
    public bool ShouldWindDown { get; init; }
    public bool TomorrowNoteDue { get; init; }
    public string WindDownSuggestion { get; init; } = "";
}

public sealed class TodayNutrition
{
    public decimal ProteinGrams { get; init; }
    public decimal FiberGrams { get; init; }
    public bool ProteinUnknown { get; init; }
    public bool FiberUnknown { get; init; }
    public bool HasEstimates { get; init; }
    public decimal PlannedProteinGrams { get; init; }
    public decimal PlannedFiberGrams { get; init; }
    public bool PlannedProteinUnknown { get; init; }
    public bool PlannedFiberUnknown { get; init; }
}

public sealed class TodayImportResult
{
    public int Imported { get; set; }
    public int Duplicates { get; set; }
    public int Rejected { get; set; }
    public List<string> Errors { get; set; } = new();
}

// Mac envelope; task IDs and inner field names remain portable. No Windows import is enabled.
public sealed class LocalFlowEnvelope
{
    [System.Text.Json.Serialization.JsonRequired]
    public int Version { get; set; } = 1;
    [System.Text.Json.Serialization.JsonRequired]
    public Guid OriginMicheId { get; set; }
    [System.Text.Json.Serialization.JsonRequired]
    public TodayDocument Document { get; set; } = new();
    public WindowGeometry? Window { get; set; }
}
