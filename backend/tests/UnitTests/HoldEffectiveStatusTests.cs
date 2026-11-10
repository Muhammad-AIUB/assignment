using Domain.Entities;
using Domain.Enums;

namespace UnitTests;

/// <summary>
/// Expiry rule, domain half: a stored HELD hold whose time is up IS expired, whether or not anything has swept the row.
/// This single function is what GET /holds/{id} and GET /me/holds use, so its boundary is the API's boundary.
/// </summary>
public class HoldEffectiveStatusTests
{
    private static readonly DateTime Now = new(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_held_hold_before_its_expiry_is_still_held()
    {
        Assert.Equal(HoldStatus.Held, Hold.EffectiveStatus(HoldStatus.Held, Now.AddTicks(1), Now));
        Assert.Equal(HoldStatus.Held, Hold.EffectiveStatus(HoldStatus.Held, Now.AddMinutes(10), Now));
    }

    [Fact]
    public void A_held_hold_is_expired_at_the_exact_expiry_instant()
    {
        // The boundary is "expiresAt <= now", the same comparison the seat-claim cleanup and the sweep use in SQL.
        Assert.Equal(HoldStatus.Expired, Hold.EffectiveStatus(HoldStatus.Held, Now, Now));
    }

    [Fact]
    public void A_held_hold_is_expired_once_the_expiry_has_passed()
    {
        Assert.Equal(HoldStatus.Expired, Hold.EffectiveStatus(HoldStatus.Held, Now.AddTicks(-1), Now));
        Assert.Equal(HoldStatus.Expired, Hold.EffectiveStatus(HoldStatus.Held, Now.AddDays(-30), Now));
    }

    [Theory]
    [InlineData(HoldStatus.Confirmed)]
    [InlineData(HoldStatus.Cancelled)]
    [InlineData(HoldStatus.Expired)]
    public void Every_status_other_than_held_is_reported_as_stored_whatever_the_time(HoldStatus stored)
    {
        // A CONFIRMED admission never turns EXPIRED, and a CANCELLED hold never turns into anything else.
        Assert.Equal(stored, Hold.EffectiveStatus(stored, Now.AddDays(-30), Now));
        Assert.Equal(stored, Hold.EffectiveStatus(stored, Now, Now));
        Assert.Equal(stored, Hold.EffectiveStatus(stored, Now.AddDays(30), Now));
    }

    [Fact]
    public void A_new_hold_starts_out_held_with_a_prefixed_unique_id()
    {
        var first = new Hold();
        var second = new Hold();

        Assert.Equal(HoldStatus.Held, first.Status);
        Assert.Null(first.ConfirmedAt);
        Assert.StartsWith("h_", first.Id);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void A_new_class_starts_with_no_held_or_confirmed_seats_and_a_prefixed_unique_id()
    {
        var first = new Class();
        var second = new Class();

        Assert.Equal(0, first.HeldSeats);
        Assert.Equal(0, first.ConfirmedSeats);
        Assert.StartsWith("c_", first.Id);
        Assert.NotEqual(first.Id, second.Id);
    }
}
