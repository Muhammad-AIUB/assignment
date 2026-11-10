using Api.Services;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// The one-active-hold rule: a student may have at most one HELD or CONFIRMED hold per class. A hold that was cancelled
/// or has expired is no longer active and never blocks a new one. The rule is per class, and per student. The real
/// services run against real Postgres; the database-level backstop is tested directly.
/// (Concurrent attempts are the concurrency rule, which is tested separately.)
/// </summary>
[Collection("api")]
public class OneActiveHoldRuleTests(ApiFactory factory)
{
    private readonly FakeClock _clock = new();

    private ServiceHarness Svc() => new(factory, _clock);

    private static string Student() => ServiceHarness.NewStudent();

    // ---------- the rule is per student and per class ----------

    [Fact]
    public async Task A_student_can_hold_a_seat_in_each_of_two_different_classes_at_the_same_time()
    {
        var svc = Svc();
        var first = await svc.CreateClassAsync(3);
        var second = await svc.CreateClassAsync(3);
        var student = Student();

        var inFirst = await svc.MustHoldAsync(first, student);
        var inSecond = await svc.MustHoldAsync(second, student);

        Assert.Equal((1, 0, 2), await svc.ViewAsync(first));
        Assert.Equal((1, 0, 2), await svc.ViewAsync(second));
        var mine = await svc.MyHoldsAsync(student);
        Assert.Equal(new[] { inFirst.HoldId, inSecond.HoldId }.Order(), mine.Select(h => h.HoldId).Order());
        Assert.All(mine, h => Assert.Equal("HELD", h.Status));
    }

    [Fact]
    public async Task One_students_hold_never_blocks_another_student_in_the_same_class()
    {
        var svc = Svc();
        var classId = await svc.CreateClassAsync(3);
        var holder = Student();

        await svc.MustHoldAsync(classId, holder);
        var other = await svc.MustHoldAsync(classId, Student());
        Assert.Equal(HoldActionOutcome.Succeeded, await svc.ConfirmAsync(other.HoldId, other.StudentId));
        await svc.MustHoldAsync(classId, Student()); // a confirmed seat belonging to someone else blocks nobody either

        Assert.Equal((2, 1, 0), await svc.ViewAsync(classId));
    }

    // ---------- confirmed counts as active ----------

    [Fact]
    public async Task A_confirmed_seat_in_a_class_blocks_a_new_hold_there_but_not_in_another_class()
    {
        var svc = Svc();
        var here = await svc.CreateClassAsync(3);
        var elsewhere = await svc.CreateClassAsync(3);
        var student = Student();
        var hold = await svc.MustHoldAsync(here, student);
        Assert.Equal(HoldActionOutcome.Succeeded, await svc.ConfirmAsync(hold.HoldId, student));

        var again = await svc.HoldAsync(here, student);

        Assert.Equal(CreateHoldOutcome.DuplicateActiveHold, again.Outcome);
        Assert.Equal((0, 1, 2), await svc.ViewAsync(here)); // the refused attempt took no seat
        Assert.Equal((0, 1), await svc.CountersAsync(here));
        await svc.MustHoldAsync(elsewhere, student);
    }

    // ---------- which refusal the student sees ----------

    [Fact]
    public async Task On_a_full_class_a_student_who_already_holds_a_seat_is_told_duplicate_not_sold_out()
    {
        var svc = Svc();
        var classId = await svc.CreateClassAsync(1);
        var holder = Student();
        await svc.MustHoldAsync(classId, holder);

        var duplicate = await svc.HoldAsync(classId, holder);
        var stranger = await svc.HoldAsync(classId, Student());

        Assert.Equal(CreateHoldOutcome.DuplicateActiveHold, duplicate.Outcome);
        Assert.Equal(CreateHoldOutcome.NoSeatAvailable, stranger.Outcome);
        Assert.Single(await svc.MyHoldsAsync(holder)); // the refused attempt left no extra hold behind
        Assert.Equal((1, 0), await svc.CountersAsync(classId));
    }

    // ---------- inactive holds do not block ----------

    [Fact]
    public async Task After_cancelling_or_expiring_a_hold_the_student_can_hold_again_and_every_hold_stays_on_record()
    {
        var svc = Svc();
        var classId = await svc.CreateClassAsync(2);
        var student = Student();

        var first = await svc.MustHoldAsync(classId, student);
        Assert.Equal(HoldActionOutcome.Succeeded, await svc.CancelAsync(first.HoldId, student));

        _clock.Advance(TimeSpan.FromMinutes(1));
        var second = await svc.MustHoldAsync(classId, student); // allowed: the first is cancelled
        _clock.Advance(TimeSpan.FromSeconds(600));              // the second runs out

        _clock.Advance(TimeSpan.FromMinutes(1));
        var third = await svc.MustHoldAsync(classId, student);  // allowed: the second is expired

        Assert.Equal(3, new[] { first.HoldId, second.HoldId, third.HoldId }.Distinct().Count());
        var mine = await svc.MyHoldsAsync(student);
        Assert.Equal([third.HoldId, second.HoldId, first.HoldId], mine.Select(h => h.HoldId).ToArray());
        Assert.Equal(["HELD", "EXPIRED", "CANCELLED"], mine.Select(h => h.Status).ToArray());
        Assert.Equal((1, 0, 1), await svc.ViewAsync(classId));
        Assert.Equal((1, 0), await svc.CountersAsync(classId));
    }

    // ---------- the database enforces it too ----------

    public static TheoryData<HoldStatus, HoldStatus, bool> StatusPairs => new()
    {
        // two active rows for the same student and class: the database refuses the second
        { HoldStatus.Held, HoldStatus.Held, false },
        { HoldStatus.Held, HoldStatus.Confirmed, false },
        { HoldStatus.Confirmed, HoldStatus.Held, false },
        { HoldStatus.Confirmed, HoldStatus.Confirmed, false },
        // an inactive row on either side never conflicts, however many there are
        { HoldStatus.Cancelled, HoldStatus.Held, true },
        { HoldStatus.Expired, HoldStatus.Held, true },
        { HoldStatus.Cancelled, HoldStatus.Confirmed, true },
        { HoldStatus.Expired, HoldStatus.Confirmed, true },
        { HoldStatus.Held, HoldStatus.Cancelled, true },
        { HoldStatus.Confirmed, HoldStatus.Expired, true },
        { HoldStatus.Cancelled, HoldStatus.Cancelled, true },
        { HoldStatus.Expired, HoldStatus.Expired, true },
        { HoldStatus.Cancelled, HoldStatus.Expired, true }
    };

    [Theory]
    [MemberData(nameof(StatusPairs))]
    public async Task The_database_refuses_a_second_active_hold_for_the_same_student_and_class_and_nothing_else(
        HoldStatus existing, HoldStatus incoming, bool allowed)
    {
        var classId = await Svc().CreateClassAsync(5);
        var student = Student();

        await InsertAsync(classId, student, existing);

        if (allowed)
        {
            await InsertAsync(classId, student, incoming);
            return;
        }

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => InsertAsync(classId, student, incoming));
        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, pg.SqlState);
        Assert.Equal("ux_holds_active_student_class", pg.ConstraintName);
    }

    [Fact]
    public async Task The_database_allows_the_same_student_an_active_hold_in_every_class()
    {
        var svc = Svc();
        var student = Student();

        foreach (var classId in new[] { await svc.CreateClassAsync(2), await svc.CreateClassAsync(2), await svc.CreateClassAsync(2) })
            await InsertAsync(classId, student, HoldStatus.Held);
    }

    /// <summary>
    /// Inserts a hold row directly. The expiry is real-clock-in-the-future so the global expiry sweep, which other tests
    /// run, never touches it; the class counters are not moved because these rows exist only to probe the index.
    /// </summary>
    private Task InsertAsync(string classId, string student, HoldStatus status) => factory.WithDbAsync(async db =>
    {
        var now = DateTime.UtcNow;
        db.Holds.Add(new Hold
        {
            ClassId = classId, StudentId = student, Status = status,
            CreatedAt = now, ExpiresAt = now.AddMinutes(10),
            ConfirmedAt = status == HoldStatus.Confirmed ? now : null
        });
        await db.SaveChangesAsync();
    });
}
