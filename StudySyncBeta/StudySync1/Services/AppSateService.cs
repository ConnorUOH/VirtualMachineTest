using StudySync1.Models;

namespace StudySync1.Services;

public sealed class AppStateService
{
    private readonly IDataStore _store;
    private AppState? _state;

    public AppStateService(IDataStore store) => _store = store;

    public async Task<AppState> GetAsync()

    {
        _state ??= await _store.LoadAsync();
        return _state;
    }

    public async Task<AppState> ReloadAsync()
    {
        _state = await _store.LoadAsync();
        return _state;
    }

    public async Task ResetAsync()
    {
        _state = new AppState();
        await _store.SaveAsync(_state);
    }

    public async Task SaveAsync()
    {
        if (_state is null) return;
        await _store.SaveAsync(_state);
    }

    public async Task SeedDemoAsync()
    {
        var s = await GetAsync();
        if (s.Users.Count > 0) return;

        // --- Users (5) ---
        var alice = new UserProfile { Name = "Alice", Email = "alice@uni.ac.uk" };
        var bob = new UserProfile { Name = "Bob", Email = "bob@uni.ac.uk" };
        var chloe = new UserProfile { Name = "Chloe", Email = "chloe@uni.ac.uk" };
        var dan = new UserProfile { Name = "Dan", Email = "dan@uni.ac.uk" };
        var emily = new UserProfile { Name = "Emily", Email = "emily@uni.ac.uk" };

        s.Users.AddRange(new[] { alice, bob, chloe, dan, emily });

        // Helper: next occurrence of a weekday (this week or next) at a given hour/minute
        DateTime NextDow(DayOfWeek dow, int hour, int minute = 0)
        {
            var today = DateTime.Today;
            int diff = ((int)dow - (int)today.DayOfWeek + 7) % 7;
            var day = today.AddDays(diff).Date;
            return day.AddHours(hour).AddMinutes(minute);
        }

        void AddWeekly(Guid userId, EventType type, string title, DayOfWeek dow, int sh, int sm, int eh, int em, bool isPrivate = false)
        {
            var start = NextDow(dow, sh, sm);
            var end = NextDow(dow, eh, em);
            s.Events.Add(new CalendarEvent
            {
                OwnerUserId = userId,
                Type = type,
                Title = title,
                IsPrivate = isPrivate,
                Start = start,
                End = end,
                Recurrence = new RecurrenceRule { IsRecurringWeekly = true, DayOfWeek = dow }
            });
        }

        void AddOneOff(Guid userId, EventType type, string title, DateTime start, DateTime end, bool isPrivate = false)
        {
            s.Events.Add(new CalendarEvent
            {
                OwnerUserId = userId,
                Type = type,
                Title = title,
                IsPrivate = isPrivate,
                Start = start,
                End = end
            });
        }

        // --- Lectures (recurring, gives a "full timetable" vibe) ---
        // Alice: Mon/Wed lectures + Thu lab
        AddWeekly(alice.Id, EventType.Lecture, "Data Structures", DayOfWeek.Monday, 9, 0, 10, 30);
        AddWeekly(alice.Id, EventType.Lecture, "Machine Learning", DayOfWeek.Wednesday, 14, 0, 15, 30);
        AddWeekly(alice.Id, EventType.Lecture, "Web Development Lab", DayOfWeek.Thursday, 11, 0, 12, 30);

        // Bob: Tue/Fri lectures + Wed tutorial
        AddWeekly(bob.Id, EventType.Lecture, "Database Systems", DayOfWeek.Tuesday, 9, 0, 10, 30);
        AddWeekly(bob.Id, EventType.Lecture, "Software Engineering", DayOfWeek.Friday, 10, 0, 11, 30);
        AddWeekly(bob.Id, EventType.Lecture, "SE Tutorial", DayOfWeek.Wednesday, 12, 0, 13, 0);

        // Chloe: Mon/Tue lectures
        AddWeekly(chloe.Id, EventType.Lecture, "Networks", DayOfWeek.Monday, 11, 0, 12, 30);
        AddWeekly(chloe.Id, EventType.Lecture, "Human-Computer Interaction", DayOfWeek.Tuesday, 14, 0, 15, 30);

        // Dan: Wed/Thu lectures
        AddWeekly(dan.Id, EventType.Lecture, "Operating Systems", DayOfWeek.Wednesday, 9, 0, 10, 30);
        AddWeekly(dan.Id, EventType.Lecture, "AI Foundations", DayOfWeek.Thursday, 9, 0, 10, 30);

        // Emily: Tue/Thu lectures + Fri lab
        AddWeekly(emily.Id, EventType.Lecture, "Algorithms", DayOfWeek.Tuesday, 11, 0, 12, 30);
        AddWeekly(emily.Id, EventType.Lecture, "UX Design", DayOfWeek.Thursday, 14, 0, 15, 30);
        AddWeekly(emily.Id, EventType.Lecture, "Algorithms Lab", DayOfWeek.Friday, 13, 0, 14, 30);

        // --- Regular Activities (social/sport, some private) ---
        AddWeekly(alice.Id, EventType.Activity, "Gym", DayOfWeek.Monday, 7, 0, 8, 0);
        AddWeekly(alice.Id, EventType.Activity, "Club Meeting", DayOfWeek.Wednesday, 18, 0, 19, 0);
        AddWeekly(alice.Id, EventType.Activity, "Friday Night Out", DayOfWeek.Friday, 19, 0, 22, 0, isPrivate: true);

        AddWeekly(bob.Id, EventType.Activity, "Football", DayOfWeek.Wednesday, 18, 0, 20, 0);
        AddWeekly(bob.Id, EventType.Activity, "Part-time Work", DayOfWeek.Saturday, 10, 0, 14, 0, isPrivate: true);

        AddWeekly(chloe.Id, EventType.Activity, "Society Event", DayOfWeek.Thursday, 18, 0, 20, 0);
        AddWeekly(chloe.Id, EventType.Activity, "Study Cafe", DayOfWeek.Sunday, 14, 0, 16, 0);

        AddWeekly(dan.Id, EventType.Activity, "Basketball", DayOfWeek.Tuesday, 18, 0, 20, 0);
        AddWeekly(dan.Id, EventType.Activity, "Family Time", DayOfWeek.Sunday, 12, 0, 14, 0, isPrivate: true);

        AddWeekly(emily.Id, EventType.Activity, "Dance Class", DayOfWeek.Monday, 18, 0, 19, 30);
        AddWeekly(emily.Id, EventType.Activity, "Dinner Plans", DayOfWeek.Friday, 18, 0, 20, 0, isPrivate: true);

        // --- Assignments (varied workloads + due dates) ---
        s.Assignments.AddRange(new[]
        {
        new Assignment
        {
            OwnerUserId = alice.Id,
            Title = "Algorithm Quiz",
            DueDate = DateTime.Today.AddDays(7).Date.AddHours(17),
            EstimatedHours = 4
        },
        new Assignment
        {
            OwnerUserId = alice.Id,
            Title = "ML Paper Summary",
            DueDate = DateTime.Today.AddDays(12).Date.AddHours(17),
            EstimatedHours = 6
        },
        new Assignment
        {
            OwnerUserId = bob.Id,
            Title = "Database Design Doc",
            DueDate = DateTime.Today.AddDays(10).Date.AddHours(17),
            EstimatedHours = 5
        },
        new Assignment
        {
            OwnerUserId = chloe.Id,
            Title = "HCI Prototype Review",
            DueDate = DateTime.Today.AddDays(9).Date.AddHours(12),
            EstimatedHours = 3
        },
        new Assignment
        {
            OwnerUserId = dan.Id,
            Title = "OS Lab Report",
            DueDate = DateTime.Today.AddDays(14).Date.AddHours(17),
            EstimatedHours = 7
        },
        new Assignment
        {
            OwnerUserId = emily.Id,
            Title = "Algorithms Problem Sheet",
            DueDate = DateTime.Today.AddDays(8).Date.AddHours(17),
            EstimatedHours = 4
        }
    });

        // --- Groups (Team Alpha with all 5) ---
        s.Groups.Add(new Group
        {
            Name = "Team Alpha",
            MemberUserIds = new List<Guid> { alice.Id, bob.Id, chloe.Id, dan.Id, emily.Id }
        });

        // --- A couple of one-off events (so it doesn't look *only* recurring) ---
        var nextSat = NextDow(DayOfWeek.Saturday, 0);
        AddOneOff(alice.Id, EventType.Activity, "Doctor Appointment", nextSat.AddHours(9), nextSat.AddHours(10), isPrivate: true);
        AddOneOff(bob.Id, EventType.Activity, "Birthday Lunch", nextSat.AddDays(7).AddHours(12), nextSat.AddDays(7).AddHours(14));

        await SaveAsync();
    }
}
