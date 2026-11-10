using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// The published OpenAPI document (what Swagger UI renders) must say what docs/API_CONTRACT.md says: the same
/// operations, the same status codes, the same field names, a mandatory X-Student-Id where the contract needs one,
/// and JSON only. GET /health is an operational probe outside the contract and is expected as the one extra.
/// </summary>
[Collection("api")]
public class SwaggerDocumentTests(ApiFactory factory)
{
    private static readonly string[] OperationMethods = ["get", "post", "put", "delete", "patch"];

    // Every operation the API exposes and the status codes it documents: the contract's success code plus the 4xx
    // the contract lists (or implies through the X-Student-Id / validation rules).
    private static readonly Dictionary<string, string[]> StatusCodes = new()
    {
        ["POST /classes"] = ["201", "400"],
        ["GET /classes"] = ["200"],
        ["GET /classes/{classId}"] = ["200", "404"],
        ["POST /classes/{classId}/holds"] = ["201", "400", "404", "409"],
        ["GET /holds/{holdId}"] = ["200", "404"],
        ["POST /holds/{holdId}/confirm"] = ["200", "400", "404", "409"],
        ["DELETE /holds/{holdId}"] = ["204", "400", "404", "409"],
        ["GET /me/holds"] = ["200", "400"],
        ["GET /health"] = ["200", "503"],
    };

    private static readonly string[] OperationsNeedingStudentId =
    [
        "POST /classes/{classId}/holds",
        "POST /holds/{holdId}/confirm",
        "DELETE /holds/{holdId}",
        "GET /me/holds",
    ];

    // Success response of each operation: status, schema name, and whether the body is a list of that schema.
    private static readonly Dictionary<string, (string Status, string Schema, bool IsList)> SuccessResponses = new()
    {
        ["POST /classes"] = ("201", "ClassResponse", false),
        ["GET /classes"] = ("200", "ClassSummaryResponse", true),
        ["GET /classes/{classId}"] = ("200", "ClassSummaryResponse", false),
        ["POST /classes/{classId}/holds"] = ("201", "HoldResponse", false),
        ["GET /holds/{holdId}"] = ("200", "HoldStatusResponse", false),
        ["POST /holds/{holdId}/confirm"] = ("200", "ConfirmHoldResponse", false),
        ["GET /me/holds"] = ("200", "MyHoldResponse", true),
        ["GET /health"] = ("200", "HealthResponse", false),
    };

    // Field names exactly as API_CONTRACT.md spells them (plus the standard error shape and the health body).
    private static readonly Dictionary<string, string[]> SchemaFields = new()
    {
        ["CreateClassRequest"] = ["name", "totalSeats"],
        ["ClassResponse"] = ["id", "name", "totalSeats"],
        ["ClassSummaryResponse"] = ["id", "name", "totalSeats", "heldSeats", "confirmedSeats", "availableSeats"],
        ["HoldResponse"] = ["holdId", "classId", "studentId", "status", "expiresAt"],
        ["HoldStatusResponse"] = ["holdId", "classId", "status", "expiresAt"],
        ["ConfirmHoldResponse"] = ["holdId", "status"],
        ["MyHoldResponse"] = ["holdId", "classId", "className", "status", "expiresAt"],
        ["ErrorResponse"] = ["error", "message"],
        ["HealthResponse"] = ["status", "database"],
    };

    public static IEnumerable<object[]> OperationKeys() => StatusCodes.Keys.Select(k => new object[] { k });
    public static IEnumerable<object[]> SuccessKeys() => SuccessResponses.Keys.Select(k => new object[] { k });
    public static IEnumerable<object[]> SchemaNames() => SchemaFields.Keys.Select(k => new object[] { k });

    private async Task<JsonElement> LoadDocumentAsync()
    {
        var res = await factory.CreateClient().GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Dictionary<string, JsonElement> OperationsOf(JsonElement doc)
    {
        var found = new Dictionary<string, JsonElement>();
        foreach (var path in doc.GetProperty("paths").EnumerateObject())
            foreach (var op in path.Value.EnumerateObject().Where(o => OperationMethods.Contains(o.Name)))
                found[$"{op.Name.ToUpperInvariant()} {path.Name}"] = op.Value;
        return found;
    }

    private static JsonElement Operation(JsonElement doc, string key)
    {
        var ops = OperationsOf(doc);
        Assert.True(ops.ContainsKey(key), $"'{key}' is not in the OpenAPI document. Found: {string.Join(", ", ops.Keys)}");
        return ops[key];
    }

    private static string SchemaRefName(JsonElement schema) => schema.GetProperty("$ref").GetString()!.Split('/').Last();

    [Fact]
    public async Task The_document_lists_exactly_the_eight_contract_operations_and_the_health_probe()
    {
        var doc = await LoadDocumentAsync();

        Assert.Equal(StatusCodes.Keys.Order(), OperationsOf(doc).Keys.Order());
    }

    [Fact]
    public async Task The_document_and_the_swagger_ui_page_are_served_with_a_real_title()
    {
        var doc = await LoadDocumentAsync();
        var info = doc.GetProperty("info");

        Assert.Equal("School Admission API", info.GetProperty("title").GetString());
        var description = info.TryGetProperty("description", out var d) ? d.GetString() : null;
        Assert.False(string.IsNullOrWhiteSpace(description), "The API has no description.");

        var ui = await factory.CreateClient().GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, ui.StatusCode);
        Assert.Equal("text/html", ui.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [MemberData(nameof(OperationKeys))]
    public async Task An_operation_documents_exactly_the_expected_status_codes(string key)
    {
        var op = Operation(await LoadDocumentAsync(), key);

        var documented = op.GetProperty("responses").EnumerateObject().Select(r => r.Name).Order().ToArray();

        Assert.Equal(StatusCodes[key].Order(), documented);
    }

    [Theory]
    [MemberData(nameof(OperationKeys))]
    public async Task X_Student_Id_is_a_required_17_digit_header_on_exactly_the_operations_that_need_it(string key)
    {
        var op = Operation(await LoadDocumentAsync(), key);

        var header = op.TryGetProperty("parameters", out var ps)
            ? ps.EnumerateArray().Where(p => p.GetProperty("in").GetString() == "header").ToList()
            : [];

        if (!OperationsNeedingStudentId.Contains(key))
        {
            Assert.Empty(header);
            return;
        }

        var studentId = Assert.Single(header);
        Assert.Equal("X-Student-Id", studentId.GetProperty("name").GetString());
        Assert.True(studentId.TryGetProperty("required", out var required) && required.GetBoolean(),
            "X-Student-Id is mandatory (400 without it), so the document must mark it required.");
        Assert.Equal("^[0-9]{17}$", studentId.GetProperty("schema").GetProperty("pattern").GetString());
        Assert.False(string.IsNullOrWhiteSpace(studentId.GetProperty("description").GetString()));
    }

    [Theory]
    [MemberData(nameof(OperationKeys))]
    public async Task Bodies_are_documented_as_application_json_only(string key)
    {
        var op = Operation(await LoadDocumentAsync(), key);

        var mediaTypes = new List<string>();
        if (op.TryGetProperty("requestBody", out var body))
            mediaTypes.AddRange(body.GetProperty("content").EnumerateObject().Select(c => c.Name));
        foreach (var response in op.GetProperty("responses").EnumerateObject())
            if (response.Value.TryGetProperty("content", out var content))
                mediaTypes.AddRange(content.EnumerateObject().Select(c => c.Name));

        Assert.NotEmpty(mediaTypes);
        Assert.All(mediaTypes, m => Assert.Equal("application/json", m));
    }

    [Fact]
    public async Task Post_classes_documents_a_required_json_body_and_no_other_operation_takes_one()
    {
        var doc = await LoadDocumentAsync();

        foreach (var (key, op) in OperationsOf(doc))
        {
            if (key != "POST /classes")
            {
                Assert.False(op.TryGetProperty("requestBody", out _), $"{key} should not document a request body.");
                continue;
            }

            var body = op.GetProperty("requestBody");
            Assert.True(body.TryGetProperty("required", out var required) && required.GetBoolean(),
                "The body of POST /classes is mandatory, so the document must mark it required.");
            Assert.Equal("CreateClassRequest",
                SchemaRefName(body.GetProperty("content").GetProperty("application/json").GetProperty("schema")));
        }
    }

    [Theory]
    [MemberData(nameof(SuccessKeys))]
    public async Task The_success_response_points_at_the_contract_schema(string key)
    {
        var op = Operation(await LoadDocumentAsync(), key);
        var (status, schemaName, isList) = SuccessResponses[key];

        var schema = op.GetProperty("responses").GetProperty(status)
            .GetProperty("content").GetProperty("application/json").GetProperty("schema");

        if (isList)
        {
            Assert.Equal("array", schema.GetProperty("type").GetString());
            schema = schema.GetProperty("items");
        }

        Assert.Equal(schemaName, SchemaRefName(schema));
    }

    [Theory]
    [MemberData(nameof(SchemaNames))]
    public async Task A_schema_has_exactly_the_field_names_the_contract_spells(string name)
    {
        var doc = await LoadDocumentAsync();

        var fields = doc.GetProperty("components").GetProperty("schemas").GetProperty(name)
            .GetProperty("properties").EnumerateObject().Select(p => p.Name).ToArray();

        Assert.Equal(SchemaFields[name].Order(), fields.Order());
    }

    [Fact]
    public async Task The_delete_success_response_is_a_204_with_no_body()
    {
        var op = Operation(await LoadDocumentAsync(), "DELETE /holds/{holdId}");

        Assert.False(op.GetProperty("responses").GetProperty("204").TryGetProperty("content", out _));
    }

    [Theory]
    [MemberData(nameof(OperationKeys))]
    public async Task An_operation_has_a_summary_and_every_error_response_says_which_error_code_it_means(string key)
    {
        var op = Operation(await LoadDocumentAsync(), key);

        var summary = op.TryGetProperty("summary", out var s) ? s.GetString() : null;
        Assert.False(string.IsNullOrWhiteSpace(summary), $"{key} has no summary.");

        // 4xx/5xx descriptions must name the error code a client will see (e.g. NO_SEAT_AVAILABLE), not just
        // repeat the reason phrase ("Conflict").
        var errorCode = new Regex("[A-Z]{3,}(_[A-Z]{2,})+");
        foreach (var response in op.GetProperty("responses").EnumerateObject().Where(r => r.Name.StartsWith('4') || r.Name.StartsWith('5')))
        {
            var description = response.Value.GetProperty("description").GetString() ?? "";
            Assert.True(errorCode.IsMatch(description),
                $"{key} {response.Name} description '{description}' does not name an error code.");
        }
    }
}
