using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Entities;
using Domain.Enums;
using Xunit;

namespace IntegrationTests;

[Collection("api")]
public class ClassesEndpointsTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> CreateClassAsync(string name, int seats)
    {
        var res = await _client.PostAsJsonAsync("/classes", new { name, totalSeats = seats });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    private static async Task AssertError(HttpResponseMessage res, HttpStatusCode status, string code)
    {
        Assert.Equal(status, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("error").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task Post_creates_class_and_returns_201_with_contract_shape()
    {
        var res = await _client.PostAsJsonAsync("/classes", new { name = "Class 6 - Morning Shift", totalSeats = 120 });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.StartsWith("c_", body.GetProperty("id").GetString());
        Assert.Equal("Class 6 - Morning Shift", body.GetProperty("name").GetString());
        Assert.Equal(120, body.GetProperty("totalSeats").GetInt32());
        Assert.Equal(3, body.EnumerateObject().Count());
    }

    [Theory]
    [InlineData("", 10)]
    [InlineData("   ", 10)]
    [InlineData("Ok", 0)]
    [InlineData("Ok", -5)]
    public async Task Post_rejects_invalid_input_with_400_error_shape(string name, int seats)
    {
        var res = await _client.PostAsJsonAsync("/classes", new { name, totalSeats = seats });
        await AssertError(res, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Post_rejects_missing_name_and_malformed_body_with_400()
    {
        var missing = await _client.PostAsJsonAsync("/classes", new { totalSeats = 10 });
        await AssertError(missing, HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        var bad = await _client.PostAsync("/classes",
            new StringContent("{not json", System.Text.Encoding.UTF8, "application/json"));
        await AssertError(bad, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    [Fact]
    public async Task Get_unknown_class_returns_404_error_shape()
    {
        var res = await _client.GetAsync("/classes/c_does_not_exist");
        await AssertError(res, HttpStatusCode.NotFound, "CLASS_NOT_FOUND");
    }

    [Fact]
    public async Task Get_and_list_compute_seat_counts_excluding_expired_holds()
    {
        var id = await CreateClassAsync("Counts class", 10);
        var now = DateTime.UtcNow;

        await factory.WithDbAsync(async db =>
        {
            Hold H(HoldStatus s, DateTime exp, string student) => new()
            {
                ClassId = id, StudentId = student, Status = s, CreatedAt = now, ExpiresAt = exp,
                ConfirmedAt = s == HoldStatus.Confirmed ? now : null
            };
            db.Holds.AddRange(
                H(HoldStatus.Held, now.AddMinutes(5), "10000000000000001"),   // counts as held
                H(HoldStatus.Held, now.AddMinutes(5), "10000000000000002"),   // counts as held
                H(HoldStatus.Held, now.AddMinutes(-5), "10000000000000003"),  // expired, unswept: ignored
                H(HoldStatus.Expired, now.AddMinutes(-5), "10000000000000004"),
                H(HoldStatus.Cancelled, now.AddMinutes(5), "10000000000000005"),
                H(HoldStatus.Confirmed, now.AddMinutes(-60), "10000000000000006"),
                H(HoldStatus.Confirmed, now.AddMinutes(-60), "10000000000000007"),
                H(HoldStatus.Confirmed, now.AddMinutes(-60), "10000000000000008"));
            await db.SaveChangesAsync();
        });

        var one = await _client.GetFromJsonAsync<JsonElement>($"/classes/{id}");
        Assert.Equal(10, one.GetProperty("totalSeats").GetInt32());
        Assert.Equal(2, one.GetProperty("heldSeats").GetInt32());
        Assert.Equal(3, one.GetProperty("confirmedSeats").GetInt32());
        Assert.Equal(5, one.GetProperty("availableSeats").GetInt32());

        var list = await _client.GetFromJsonAsync<JsonElement>("/classes");
        var item = list.EnumerateArray().Single(e => e.GetProperty("id").GetString() == id);
        Assert.Equal(2, item.GetProperty("heldSeats").GetInt32());
        Assert.Equal(3, item.GetProperty("confirmedSeats").GetInt32());
        Assert.Equal(5, item.GetProperty("availableSeats").GetInt32());
    }

    [Fact]
    public async Task List_returns_empty_counts_for_class_without_holds()
    {
        var id = await CreateClassAsync("Empty class", 7);
        var list = await _client.GetFromJsonAsync<JsonElement>("/classes");
        var item = list.EnumerateArray().Single(e => e.GetProperty("id").GetString() == id);
        Assert.Equal(0, item.GetProperty("heldSeats").GetInt32());
        Assert.Equal(0, item.GetProperty("confirmedSeats").GetInt32());
        Assert.Equal(7, item.GetProperty("availableSeats").GetInt32());
    }
}
