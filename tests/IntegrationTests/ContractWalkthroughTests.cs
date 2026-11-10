using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// docs/API_CONTRACT.md followed from top to bottom against the running API: all eight endpoints, in contract order, each
/// asserted for its exact status code and for exactly the field names the contract lists (no missing field, no extra one).
/// The error cases of every endpoint are asserted in ContractErrorCodesTests; this is the success path of the whole contract.
/// </summary>
[Collection("api")]
public class ContractWalkthroughTests(ApiFactory factory)
{
    private const string StudentA = "20152691234567890";
    private const string StudentB = "20152691234567891";

    private static string[] Fields(JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();

    private static async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(
        HttpClient client, HttpMethod method, string url, string? student = null, object? json = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (student is not null) request.Headers.Add("X-Student-Id", student);
        if (json is not null) request.Content = JsonContent.Create(json);
        var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonSerializer.Deserialize<JsonElement>(text));
    }

    [Fact]
    public async Task All_eight_endpoints_answer_with_exactly_the_status_codes_and_fields_of_the_contract()
    {
        var client = factory.CreateClient();
        var className = $"Contract walkthrough {Guid.NewGuid():N}";

        // 1. POST /classes -> 201 { id, name, totalSeats }
        var (status, body) = await SendAsync(client, HttpMethod.Post, "/classes", json: new { name = className, totalSeats = 2 });
        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(["id", "name", "totalSeats"], Fields(body));
        var classId = body.GetProperty("id").GetString()!;
        Assert.StartsWith("c_", classId);
        Assert.Equal(className, body.GetProperty("name").GetString());
        Assert.Equal(2, body.GetProperty("totalSeats").GetInt32());

        // 2. GET /classes -> 200 [ { id, name, totalSeats, heldSeats, confirmedSeats, availableSeats } ]
        (status, body) = await SendAsync(client, HttpMethod.Get, "/classes");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        var listed = Assert.Single(body.EnumerateArray(), c => c.GetProperty("id").GetString() == classId);
        string[] classFields = ["availableSeats", "confirmedSeats", "heldSeats", "id", "name", "totalSeats"];
        Assert.Equal(classFields, Fields(listed));

        // 3. GET /classes/{classId} -> 200, same shape as one list item
        (status, body) = await SendAsync(client, HttpMethod.Get, $"/classes/{classId}");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(classFields, Fields(body));
        AssertSeats(body, held: 0, confirmed: 0, available: 2);

        // 4. POST /classes/{classId}/holds -> 201 { holdId, classId, studentId, status: HELD, expiresAt }
        var before = DateTime.UtcNow;
        (status, body) = await SendAsync(client, HttpMethod.Post, $"/classes/{classId}/holds", StudentA);
        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(["classId", "expiresAt", "holdId", "status", "studentId"], Fields(body));
        var holdId = body.GetProperty("holdId").GetString()!;
        Assert.StartsWith("h_", holdId);
        Assert.Equal(classId, body.GetProperty("classId").GetString());
        Assert.Equal(StudentA, body.GetProperty("studentId").GetString());
        Assert.Equal("HELD", body.GetProperty("status").GetString());
        var expiresAt = body.GetProperty("expiresAt").GetString()!;
        Assert.EndsWith("Z", expiresAt); // UTC, as in the contract's example
        Assert.True(DateTime.Parse(expiresAt, null, System.Globalization.DateTimeStyles.AdjustToUniversal) > before);
        AssertSeats((await SendAsync(client, HttpMethod.Get, $"/classes/{classId}")).Body, held: 1, confirmed: 0, available: 1);

        // 5. GET /holds/{holdId} -> 200 { holdId, classId, status, expiresAt }
        (status, body) = await SendAsync(client, HttpMethod.Get, $"/holds/{holdId}");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["classId", "expiresAt", "holdId", "status"], Fields(body));
        Assert.Equal("HELD", body.GetProperty("status").GetString());

        // 8. GET /me/holds -> 200 [ { holdId, classId, className, status, expiresAt } ]
        (status, body) = await SendAsync(client, HttpMethod.Get, "/me/holds", StudentA);
        Assert.Equal(HttpStatusCode.OK, status);
        var mine = Assert.Single(body.EnumerateArray(), h => h.GetProperty("holdId").GetString() == holdId);
        Assert.Equal(["classId", "className", "expiresAt", "holdId", "status"], Fields(mine)); // ordinal order: 'I' sorts before 'N'
        Assert.Equal(className, mine.GetProperty("className").GetString());

        // 6. POST /holds/{holdId}/confirm -> 200 { holdId, status: CONFIRMED }; confirming again is 200 and counts nothing twice
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            (status, body) = await SendAsync(client, HttpMethod.Post, $"/holds/{holdId}/confirm", StudentA);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(["holdId", "status"], Fields(body));
            Assert.Equal(holdId, body.GetProperty("holdId").GetString());
            Assert.Equal("CONFIRMED", body.GetProperty("status").GetString());
            AssertSeats((await SendAsync(client, HttpMethod.Get, $"/classes/{classId}")).Body, held: 0, confirmed: 1, available: 1);
        }

        // 7. DELETE /holds/{holdId} -> 204 with no body; the seat comes straight back
        (status, body) = await SendAsync(client, HttpMethod.Post, $"/classes/{classId}/holds", StudentB);
        Assert.Equal(HttpStatusCode.Created, status);
        var secondHold = body.GetProperty("holdId").GetString()!;
        AssertSeats((await SendAsync(client, HttpMethod.Get, $"/classes/{classId}")).Body, held: 1, confirmed: 1, available: 0);

        (status, body) = await SendAsync(client, HttpMethod.Delete, $"/holds/{secondHold}", StudentB);
        Assert.Equal(HttpStatusCode.NoContent, status);
        Assert.Equal(JsonValueKind.Undefined, body.ValueKind); // empty body
        AssertSeats((await SendAsync(client, HttpMethod.Get, $"/classes/{classId}")).Body, held: 0, confirmed: 1, available: 1);
        Assert.Equal("CANCELLED", (await SendAsync(client, HttpMethod.Get, $"/holds/{secondHold}")).Body.GetProperty("status").GetString());
    }

    private static void AssertSeats(JsonElement cls, int held, int confirmed, int available)
    {
        Assert.Equal(2, cls.GetProperty("totalSeats").GetInt32());
        Assert.Equal(held, cls.GetProperty("heldSeats").GetInt32());
        Assert.Equal(confirmed, cls.GetProperty("confirmedSeats").GetInt32());
        Assert.Equal(available, cls.GetProperty("availableSeats").GetInt32());
    }
}
