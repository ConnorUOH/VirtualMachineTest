using StudySync1.Models;

namespace StudySync1.Services;

public interface IScheduleService
{
    Task<int> GenerateStudyPlanForWeekAsync(Guid userId, DateTime weekStart);
    Task<int> ClearStudyBlocksForWeekAsync(Guid userId, DateTime weekStart);
    Task<int> ResolveConflictsForWeekAsync(Guid userId, DateTime weekStart, Guid newActivityId);
}

public sealed class ScheduleService : IScheduleService
{
    private readonly AppStateService _state;

    // Preferences for "realistic student schedule"
    private const int StudyWindowStartHour = 14; // 14:00
    private const int StudyWindowEndHour = 21;   // 21:00 (last start is 20:00–21:00)
    private const int MaxStudyBlocksPerDay = 4;  // cap to 4 hours/day

    public ScheduleService(AppStateService state) => _state = state;

    public async Task<int> ClearStudyBlocksForWeekAsync(Guid userId, DateTime weekStart)
    {
        var app = await _state.GetAsync();
        var weekEnd = weekStart.Date.AddDays(7);

        int before = app.Events.Count;

        app.Events.RemoveAll(e =>
            e.OwnerUserId == userId
            && (e.Type == EventType.Study || e.Type == EventType.MakeUp)
            && e.Start < weekEnd && e.End > weekStart);

        await _state.SaveAsync();
        return before - app.Events.Count;
    }

    public async Task<int> GenerateStudyPlanForWeekAsync(Guid userId, DateTime weekStart)
    {
        var app = await _state.GetAsync();
        var weekEnd = weekStart.Date.AddDays(7);

        // Make generation idempotent for demo: clear existing Study/MakeUp first
        await ClearStudyBlocksForWeekAsync(userId, weekStart);

        // Assignments for the user
        var assignments = app.Assignments
            .Where(a => a.OwnerUserId == userId)
            .OrderBy(a => a.DueDate)
            .ToList();

        if (assignments.Count == 0)
            return 0;

        // Expand recurring events and treat them as "busy" for scheduling
        // (Lectures, Activities, GroupStudy, etc. block time)
        var busyEvents = ExpandRecurring(app.Events.Where(e => e.OwnerUserId == userId), weekStart, weekEnd)
            .Where(e => e.Type != EventType.Study && e.Type != EventType.MakeUp) // these were cleared anyway
            .ToList();

        var busy = busyEvents.Select(e => (e.Start, e.End)).OrderBy(x => x.Start).ToList();

        // Build free 1-hour slots PER DAY in preferred window (14:00–21:00)
        var daySlots = new Dictionary<DateTime, Queue<(DateTime Start, DateTime End)>>();

        for (var day = weekStart.Date; day < weekEnd.Date; day = day.AddDays(1))
        {
            var slots = new List<(DateTime Start, DateTime End)>();

            var t = day.AddHours(StudyWindowStartHour);
            var endDay = day.AddHours(StudyWindowEndHour);

            while (t.AddHours(1) <= endDay)
            {
                var slot = (Start: t, End: t.AddHours(1));
                if (!OverlapsAny(slot, busy))
                    slots.Add(slot);

                t = t.AddHours(1);
            }

            daySlots[day] = new Queue<(DateTime Start, DateTime End)>(slots.OrderBy(s => s.Start));
        }

        // Track how many we place per day to enforce the 4-per-day cap
        var perDayCount = daySlots.Keys.ToDictionary(d => d, _ => 0);

        int created = 0;
        var daysInWeek = daySlots.Keys.OrderBy(d => d).ToList();
        int dayIndex = 0;

        foreach (var a in assignments)
        {
            int blocksNeeded = (int)Math.Ceiling(a.EstimatedHours);

            for (int i = 0; i < blocksNeeded; i++)
            {
                // Find next day with available slots AND not over daily cap
                int tries = 0;
                while (tries < daysInWeek.Count)
                {
                    var day = daysInWeek[dayIndex];

                    bool hasSlot = daySlots[day].Count > 0;
                    bool underCap = perDayCount[day] < MaxStudyBlocksPerDay;

                    if (hasSlot && underCap)
                        break;

                    dayIndex = (dayIndex + 1) % daysInWeek.Count;
                    tries++;
                }

                // No valid day left (everything full or capped)
                var chosenDay = daysInWeek[dayIndex];
                if (daySlots[chosenDay].Count == 0 || perDayCount[chosenDay] >= MaxStudyBlocksPerDay)
                    break;

                var slot = daySlots[chosenDay].Dequeue();

                app.Events.Add(new CalendarEvent
                {
                    OwnerUserId = userId,
                    Type = EventType.Study,
                    Title = $"Study: {a.Title}",
                    Start = slot.Start,
                    End = slot.End,
                    AssignmentId = a.Id
                });

                perDayCount[chosenDay]++;
                created++;

                // Move to next day for next block to spread workload
                dayIndex = (dayIndex + 1) % daysInWeek.Count;
            }
        }

        await _state.SaveAsync();
        return created;
    }

    public async Task<int> ResolveConflictsForWeekAsync(Guid userId, DateTime weekStart, Guid newActivityId)
    {
        var app = await _state.GetAsync();
        var weekEnd = weekStart.Date.AddDays(7);

        var activity = app.Events.FirstOrDefault(e =>
            e.Id == newActivityId && e.OwnerUserId == userId && e.Type == EventType.Activity);

        if (activity is null)
            return 0;

        // Expand recurring to find overlaps correctly
        var expanded = ExpandRecurring(app.Events.Where(e => e.OwnerUserId == userId), weekStart, weekEnd).ToList();

        // Find study/makeup blocks overlapping the new activity
        var overlappingStudy = expanded
            .Where(e => (e.Type == EventType.Study || e.Type == EventType.MakeUp)
                        && e.Start < activity.End && e.End > activity.Start)
            .ToList();

        if (overlappingStudy.Count == 0)
            return 0;

        // Remove the stored versions by Id
        var overlapIds = overlappingStudy.Select(s => s.Id).Distinct().ToHashSet();
        app.Events.RemoveAll(e => e.OwnerUserId == userId && overlapIds.Contains(e.Id));

        // How many hours were lost (round up to 1-hour blocks)
        var lostHours = overlappingStudy.Sum(s => (s.End - s.Start).TotalHours);
        int blocksNeeded = (int)Math.Ceiling(lostHours);

        // Busy includes ALL events now (including existing Study/MakeUp/GroupStudy),
        // so make-up blocks never stack on something else.
        expanded = ExpandRecurring(app.Events.Where(e => e.OwnerUserId == userId), weekStart, weekEnd).ToList();
        var busy = expanded.Select(e => (e.Start, e.End)).OrderBy(x => x.Start).ToList();

        // Find free 1-hour slots in preferred window (14:00–21:00), any day, max 4/day
        var daySlots = new Dictionary<DateTime, Queue<(DateTime Start, DateTime End)>>();
        var perDayCount = new Dictionary<DateTime, int>();

        for (var day = weekStart.Date; day < weekEnd.Date; day = day.AddDays(1))
        {
            var slots = new List<(DateTime Start, DateTime End)>();

            var t = day.AddHours(StudyWindowStartHour);
            var endDay = day.AddHours(StudyWindowEndHour);

            while (t.AddHours(1) <= endDay)
            {
                var slot = (Start: t, End: t.AddHours(1));
                if (!OverlapsAny(slot, busy))
                    slots.Add(slot);

                t = t.AddHours(1);
            }

            daySlots[day] = new Queue<(DateTime Start, DateTime End)>(slots.OrderBy(s => s.Start));
            perDayCount[day] = 0;
        }

        int created = 0;
        var daysInWeek = daySlots.Keys.OrderBy(d => d).ToList();
        int dayIndex = 0;

        for (int i = 0; i < blocksNeeded; i++)
        {
            // Find a day with capacity + slots
            int tries = 0;
            while (tries < daysInWeek.Count)
            {
                var d = daysInWeek[dayIndex];
                if (daySlots[d].Count > 0 && perDayCount[d] < MaxStudyBlocksPerDay)
                    break;

                dayIndex = (dayIndex + 1) % daysInWeek.Count;
                tries++;
            }

            var chosen = daysInWeek[dayIndex];
            if (daySlots[chosen].Count == 0 || perDayCount[chosen] >= MaxStudyBlocksPerDay)
                break;

            var slot = daySlots[chosen].Dequeue();

            app.Events.Add(new CalendarEvent
            {
                OwnerUserId = userId,
                Type = EventType.MakeUp,
                Title = "MakeUp Study",
                Start = slot.Start,
                End = slot.End,
                Reason = $"Moved due to activity: {activity.Title}"
            });

            perDayCount[chosen]++;
            created++;
            dayIndex = (dayIndex + 1) % daysInWeek.Count;
        }

        await _state.SaveAsync();
        return created;
    }

    private static bool OverlapsAny((DateTime Start, DateTime End) slot, List<(DateTime Start, DateTime End)> busy)
        => busy.Any(b => slot.Start < b.End && slot.End > b.Start);

    private static IEnumerable<CalendarEvent> ExpandRecurring(IEnumerable<CalendarEvent> events, DateTime rangeStart, DateTime rangeEnd)
    {
        foreach (var e in events)
        {
            if (e.Recurrence?.IsRecurringWeekly == true && e.Recurrence.DayOfWeek is DayOfWeek dow)
            {
                var day = rangeStart.Date;
                while (day < rangeEnd.Date)
                {
                    if (day.DayOfWeek == dow)
                    {
                        var start = day.AddHours(e.Start.Hour).AddMinutes(e.Start.Minute);
                        var end = day.AddHours(e.End.Hour).AddMinutes(e.End.Minute);

                        yield return new CalendarEvent
                        {
                            Id = e.Id,
                            OwnerUserId = e.OwnerUserId,
                            Type = e.Type,
                            Title = e.Title,
                            IsPrivate = e.IsPrivate,
                            Start = start,
                            End = end,
                            AssignmentId = e.AssignmentId,
                            Reason = e.Reason,
                            Recurrence = e.Recurrence
                        };
                    }
                    day = day.AddDays(1);
                }
            }
            else
            {
                if (e.Start < rangeEnd && e.End > rangeStart)
                    yield return e;
            }
        }
    }
}