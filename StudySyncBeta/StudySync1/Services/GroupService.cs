using StudySync1.Models;

namespace StudySync1.Services;

public interface IGroupService
{
    Task<List<(DateTime Start, DateTime End)>> FindCommonFreeSlotsAsync(Guid groupId, List<Guid> memberIds, DateTime weekStart);
    Task<int> CreateGroupStudySessionAsync(Guid groupId, List<Guid> memberIds, DateTime start);
}

public sealed class GroupService : IGroupService
{
    private readonly AppStateService _state;

    public GroupService(AppStateService state) => _state = state;

    public async Task<List<(DateTime Start, DateTime End)>> FindCommonFreeSlotsAsync(Guid groupId, List<Guid> memberIds, DateTime weekStart)
    {
        var app = await _state.GetAsync();
        var weekEnd = weekStart.Date.AddDays(7);

        // 1-hour candidate slots: 08:00–22:00
        var candidateSlots = new List<(DateTime Start, DateTime End)>();
        for (var day = weekStart.Date; day < weekEnd.Date; day = day.AddDays(1))
        {
            var t = day.AddHours(8);
            var endDay = day.AddHours(22);

            while (t.AddHours(1) <= endDay)
            {
                candidateSlots.Add((t, t.AddHours(1)));
                t = t.AddHours(1);
            }
        }

        // Busy list per member (includes lectures, activities, study, make-up, group study)
        var busyByMember = new Dictionary<Guid, List<(DateTime Start, DateTime End)>>();

        foreach (var uid in memberIds.Distinct())
        {
            var expanded = ExpandRecurring(app.Events.Where(e => e.OwnerUserId == uid), weekStart, weekEnd).ToList();
            busyByMember[uid] = expanded
                .Select(e => (e.Start, e.End))
                .OrderBy(x => x.Start)
                .ToList();
        }

        // Slot is "common free" if it doesn't overlap ANY busy interval for ALL members
        var common = candidateSlots
            .Where(slot => memberIds.All(uid => !OverlapsAny(slot, busyByMember[uid])))
            .ToList();

        return common;
    }

    public async Task<int> CreateGroupStudySessionAsync(Guid groupId, List<Guid> memberIds, DateTime start)
    {
        var app = await _state.GetAsync();
        var group = app.Groups.FirstOrDefault(g => g.Id == groupId);

        var end = start.AddHours(1);
        int created = 0;

        foreach (var uid in memberIds.Distinct())
        {
            app.Events.Add(new CalendarEvent
            {
                OwnerUserId = uid,
                Type = EventType.GroupStudy,
                Title = group is null ? "Group Study" : $"Group Study: {group.Name}",
                Start = start,
                End = end,
                Reason = "Created from Group availability"
            });
            created++;
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