using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// The audit of every documented 4xx case in API_CONTRACT.md: each one is provoked through the real endpoint and must
/// answer with exactly the documented status, a stable error code, and exactly the standard shape
/// <c>{ "error", "message" }</c> (no extra fields, JSON). The same goes for the 4xx the framework produces on its own
/// (unknown route, wrong method, wrong content type), which used to come back empty or as ProblemDetails.
/// docs/BUSINESS_RULES.md does not exist, so API_CONTRACT.md is the only documented source.
/// </summary>
[Collection("api")]
public class ContractErrorCodesTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static long _nextStudent = 40_000_000_000_000_000L;
    private static string NewStudent() => Interlocked.Increment(ref _nextStudent).ToString();

    // ---------- helpers ----------

    private async Task<string> CreateClassAsync(int seats)
    {
        var res = await _client.PostAsJsonAsync("/classes", new { name = "C-" + Guid.NewGuid().ToString("N")[..8], totalSeats = seats });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? studentId)
    {
        var req = new HttpRequestMessage(method, url);
        if (studentId is not null) req.Headers.Add("X-Student-Id", studentId);
        return _client.SendAsync(req);
    }

    private Task<HttpResponseMessage> Hold(string classId, string? student) => SendAsync(HttpMethod.Post, $"/classes/{classId}/holds", student);
    private Task<HttpResponseMessage> Confirm(string holdId, string? student) => SendAsync(HttpMethod.Post, $"/holds/{holdId}/confirm", student);
    private Task<HttpResponseMessage> Cancel(string holdId, string? student) => SendAsync(HttpMethod.Delete, $"/holds/{holdId}", student);

    private async Task<string> HeldAsync(string classId, string student)
    {
        var res = await Hold(classId, student);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("holdId").GetString()!;
    }

    private async Task<string> ConfirmedAsync(string classId, string student)
    {
        var id = await HeldAsync(classId, student);
        Assert.Equal(HttpStatusCode.OK, (await Confirm(id, student)).StatusCode);
        return id;
    }

    private async Task<string> CancelledAsync(string classId, string student)
    {
        var id = await HeldAsync(classId, student);
        Assert.Equal(HttpStatusCode.NoContent, (await Cancel(id, student)).StatusCode);
        return id;
    }

    /// <summary>A HELD hold whose time is up but which no job has swept (lazy expiry must still treat it as EXPIRED).</summary>
    private async Task<string> ExpiredAsync(string classId, string student)
    {
        var hold = new Hold
        {
            ClassId = classId, StudentId = student, Status = HoldStatus.Held,
            CreatedAt = DateTime.UtcNow.AddMinutes(-20), ExpiresAt = DateTime.UtcNow.AddMinutes(-10)
        };
        await factory.WithDbAsync(async db =>
        {
            db.Holds.Add(hold);
            await db.Classes.Where(c => c.Id == classId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.HeldSeats, c => c.HeldSeats + 1));
            await db.SaveChangesAsync();
        });
        return hold.Id;
    }

    /// <summary>Status, error code, JSON content type, and no field other than error and message.</summary>
    private static async Task Expect(HttpResponseMessage res, HttpStatusCode status, string code)
    {
        Assert.Equal(status, res.StatusCode);
        Assert.StartsWith("application/json", res.Content.Headers.ContentType?.ToString());
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(["error", "message"], body.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(code, body.GetProperty("error").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
    }

    private static StringContent Json(string json, string contentType = "application/json") =>
        new(json, Encoding.UTF8, contentType);

    // ---------- 1. POST /classes ----------

    [Theory]
    [InlineData("{\"name\":\"\",\"totalSeats\":10}")]
    [InlineData("{\"name\":\"Ok\",\"totalSeats\":0}")]
    [InlineData("{\"name\":\"Ok\",\"totalSeats\":-3}")]
    [InlineData("{\"totalSeats\":10}")]
    [InlineData("{not json")]
    [InlineData("null")]
    public async Task Post_classes_with_an_invalid_body_is_400_VALIDATION_ERROR(string body) =>
        await Expect(await _client.PostAsync("/classes", Json(body)), HttpStatusCode.BadRequest, "VALIDATION_ERROR");

    [Fact]
    public async Task Post_classes_with_a_NUL_character_in_the_name_is_400_not_a_database_error()
    {
        // PostgreSQL cannot store U+0000 in text. This used to reach the database and surface as a 500.
        var res = await _client.PostAsync("/classes", Json("{\"name\":\"Class\\u0000Six\",\"totalSeats\":5}"));

        await Expect(res, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
    }

    // ---------- 3. GET /classes/{classId} ----------

    [Fact]
    public async Task Get_class_that_does_not_exist_is_404_CLASS_NOT_FOUND() =>
        await Expect(await _client.GetAsync("/classes/c_nope"), HttpStatusCode.NotFound, "CLASS_NOT_FOUND");

    // ---------- 4. POST /classes/{classId}/holds ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234567890123456")]
    [InlineData("123456789012345678")]
    [InlineData("2015269123456789x")]
    public async Task Create_hold_with_a_missing_or_malformed_student_id_is_400_INVALID_STUDENT_ID(string? id) =>
        await Expect(await Hold(await CreateClassAsync(2), id), HttpStatusCode.BadRequest, "INVALID_STUDENT_ID");

    [Fact]
    public async Task Create_hold_in_an_unknown_class_is_404_CLASS_NOT_FOUND() =>
        await Expect(await Hold("c_nope", NewStudent()), HttpStatusCode.NotFound, "CLASS_NOT_FOUND");

    [Fact]
    public async Task Create_hold_checks_the_header_before_the_class_so_a_bad_header_on_an_unknown_class_is_400() =>
        await Expect(await Hold("c_nope", "123"), HttpStatusCode.BadRequest, "INVALID_STUDENT_ID");

    [Fact]
    public async Task Create_hold_in_a_full_class_is_409_NO_SEAT_AVAILABLE()
    {
        var classId = await CreateClassAsync(1);
        await HeldAsync(classId, NewStudent());

        await Expect(await Hold(classId, NewStudent()), HttpStatusCode.Conflict, "NO_SEAT_AVAILABLE");
    }

    [Fact]
    public async Task Create_hold_when_the_student_already_has_an_active_hold_is_409_DUPLICATE_ACTIVE_HOLD()
    {
        var classId = await CreateClassAsync(5);
        var student = NewStudent();
        await HeldAsync(classId, student);

        await Expect(await Hold(classId, student), HttpStatusCode.Conflict, "DUPLICATE_ACTIVE_HOLD");
    }

    [Fact]
    public async Task Create_hold_when_the_student_already_has_a_confirmed_admission_is_409_DUPLICATE_ACTIVE_HOLD()
    {
        var classId = await CreateClassAsync(5);
        var student = NewStudent();
        await ConfirmedAsync(classId, student);

        await Expect(await Hold(classId, student), HttpStatusCode.Conflict, "DUPLICATE_ACTIVE_HOLD");
    }

    // ---------- 5. GET /holds/{holdId} ----------

    [Fact]
    public async Task Get_hold_that_does_not_exist_is_404_HOLD_NOT_FOUND() =>
        await Expect(await _client.GetAsync("/holds/h_nope"), HttpStatusCode.NotFound, "HOLD_NOT_FOUND");

    // ---------- 6. POST /holds/{holdId}/confirm ----------

    [Fact]
    public async Task Confirm_without_a_valid_student_id_is_400_INVALID_STUDENT_ID()
    {
        await Expect(await Confirm("h_x", null), HttpStatusCode.BadRequest, "INVALID_STUDENT_ID");
        await Expect(await Confirm("h_x", "12345"), HttpStatusCode.BadRequest, "INVALID_STUDENT_ID");
    }

    [Fact]
    public async Task Confirm_unknown_hold_is_404_HOLD_NOT_FOUND() =>
        await Expect(await Confirm("h_nope", NewStudent()), HttpStatusCode.NotFound, "HOLD_NOT_FOUND");

    [Fact]
    public async Task Confirm_another_students_hold_is_409_NOT_HOLD_OWNER()
    {
        var classId = await CreateClassAsync(5);
        var holdId = await HeldAsync(classId, NewStudent());

        await Expect(await Confirm(holdId, NewStudent()), HttpStatusCode.Conflict, "NOT_HOLD_OWNER");
    }

    [Fact]
    public async Task Confirm_another_students_already_confirmed_hold_is_409_NOT_HOLD_OWNER_not_the_idempotent_200()
    {
        var classId = await CreateClassAsync(5);
        var holdId = await ConfirmedAsync(classId, NewStudent());

        await Expect(await Confirm(holdId, NewStudent()), HttpStatusCode.Conflict, "NOT_HOLD_OWNER");
    }

    [Fact]
    public async Task Confirm_an_expired_hold_is_409_HOLD_EXPIRED()
    {
        var classId = await CreateClassAsync(5);
        var student = NewStudent();

        await Expect(await Confirm(await ExpiredAsync(classId, student), student), HttpStatusCode.Conflict, "HOLD_EXPIRED");
    }

    [Fact]
    public async Task Confirm_a_cancelled_hold_is_409_HOLD_CANCELLED()
    {
        var classId = await CreateClassAsync(5);
        var student = NewStudent();

        await Expect(await Confirm(await CancelledAsync(classId, student), student), HttpStatusCode.Conflict, "HOLD_CANCELLED");
    }

    [Fact]
    public async Task Re_confirming_your_own_confirmed_hold_is_the_documented_200_not_an_error()
    {
        var classId = await CreateClassAsync(5);
        var student = NewStudent();
        var holdId = await ConfirmedAsync(classId, student);

        var res = await Confirm(holdId, student);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    // ---------- 7. DELETE /holds/{holdId} ----------

    [Fact]
    public async Task Delete_without_a_valid_student_id_is_400_INVALID_STUDENT_ID()
    {
        await Expect(await Cancel("h_x", null), HttpStatusCode.BadRequest, "INVALID_STUDENT_ID");
        await Expect(await Cancel("h_x", "12345"), HttpStatusCode.BadRequest, "INVALID_STUDENT_ID");
    }

    [Fact]
    public async Task Delete_unknown_hold_is_404_HOLD_NOT_FOUND() =>
        await Expect(await Cancel("h_nope", NewStudent()), HttpStatusCode.NotFound, "HOLD_NOT_FOUND");

    [Fact]
    public async Task Delete_another_students_hold_is_409_NOT_HOLD_OWNER()
    {
        var classId = await CreateClassAsync(5);
        var holdId = await HeldAsync(classId, NewStudent());

        await Expect(await Cancel(holdId, NewStudent()), HttpStatusCode.Conflict, "NOT_HOLD_OWNER");
    }

    [Fact]
    public async Task Delete_a_confirmed_hold_is_409_HOLD_ALREADY_CONFIRMED()
    {
        var classId = await CreateClassAsync(5);
        var student = NewStudent();

        await Expect(await Cancel(await ConfirmedAsync(classId, student), student), HttpStatusCode.Conflict, "HOLD_ALREADY_CONFIRMED");
    }

    [Fact]
    public async Task Delete_a_cancelled_hold_is_409_HOLD_CANCELLED()
    {
        var classId = await CreateClassAsync(5);
        var student = NewStudent();

        await Expect(await Cancel(await CancelledAsync(classId, student), student), HttpStatusCode.Conflict, "HOLD_CANCELLED");
    }

    [Fact]
    public async Task Delete_an_expired_hold_is_409_HOLD_EXPIRED()
    {
        var classId = await CreateClassAsync(5);
        var student = NewStudent();

        await Expect(await Cancel(await ExpiredAsync(classId, student), student), HttpStatusCode.Conflict, "HOLD_EXPIRED");
    }

    // ---------- 8. GET /me/holds ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234567890123456")]
    [InlineData("123456789012345678")]
    public async Task My_holds_without_a_valid_student_id_is_400_INVALID_STUDENT_ID(string? id) =>
        await Expect(await SendAsync(HttpMethod.Get, "/me/holds", id), HttpStatusCode.BadRequest, "INVALID_STUDENT_ID");

    // ---------- 4xx the framework produces on its own ----------

    [Fact]
    public async Task An_unknown_route_is_404_ROUTE_NOT_FOUND_in_the_standard_shape() =>
        await Expect(await _client.GetAsync("/no/such/route"), HttpStatusCode.NotFound, "ROUTE_NOT_FOUND");

    [Theory]
    [InlineData("PUT", "/classes")]
    [InlineData("DELETE", "/classes/c_x")]
    [InlineData("POST", "/holds/h_x")]
    [InlineData("GET", "/holds/h_x/confirm")]
    public async Task A_known_route_with_the_wrong_method_is_405_METHOD_NOT_ALLOWED_in_the_standard_shape(string method, string url) =>
        await Expect(await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url)),
            HttpStatusCode.MethodNotAllowed, "METHOD_NOT_ALLOWED");

    [Fact]
    public async Task A_body_that_is_not_json_is_415_UNSUPPORTED_MEDIA_TYPE_in_the_standard_shape() =>
        await Expect(await _client.PostAsync("/classes", Json("{\"name\":\"x\",\"totalSeats\":5}", "text/plain")),
            HttpStatusCode.UnsupportedMediaType, "UNSUPPORTED_MEDIA_TYPE");

    [Fact]
    public async Task A_post_with_no_body_at_all_is_rejected_in_the_standard_shape() =>
        await Expect(await _client.PostAsync("/classes", null), HttpStatusCode.UnsupportedMediaType, "UNSUPPORTED_MEDIA_TYPE");
}
