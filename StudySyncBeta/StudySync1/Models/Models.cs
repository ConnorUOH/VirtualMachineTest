namespace StudySync1.Models;

public enum EventType { Lecture, Activity, Study, MakeUp, GroupStudy }

public sealed class UserProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public bool HideEventTitlesToOthers { get; set; } = true;
}

public sealed class RecurrenceRule
{
    // MVP: weekly recurrence only
    public bool IsRecurringWeekly { get; set; }
    public DayOfWeek? DayOfWeek { get; set; }
}

public sealed class CalendarEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerUserId { get; set; }
    public EventType Type { get; set; }

    public string Title { get; set; } = "";
    public bool IsPrivate { get; set; } = false;

    public DateTime Start { get; set; }
    public DateTime End { get; set; }

    public RecurrenceRule? Recurrence { get; set; }

    public Guid? AssignmentId { get; set; }
    public string? Reason { get; set; }
}

public sealed class Assignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerUserId { get; set; }
    public string Title { get; set; } = "";
    public DateTime DueDate { get; set; }
    public double EstimatedHours { get; set; }
}

public sealed class Group
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public List<Guid> MemberUserIds { get; set; } = new();
}

public sealed class AppState
{
    public List<UserProfile> Users { get; set; } = new();
    public List<CalendarEvent> Events { get; set; } = new();
    public List<Assignment> Assignments { get; set; } = new();
    public List<Group> Groups { get; set; } = new();
}