using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Api.Swagger;

/// <summary>
/// OpenAPI document settings. Everything here only changes what Swagger UI shows: no routing, binding or validation
/// behaviour is touched, so the document can describe the contract more precisely than the C# signatures can.
/// </summary>
public static class SwaggerSetup
{
    public const string StudentIdHeader = "X-Student-Id";
    public const string StudentIdPattern = "^[0-9]{17}$";

    public static void Configure(SwaggerGenOptions options)
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "School Admission API",
            Version = "v1",
            Description =
                "First-come-first-served school admission. A student **holds** a seat in a class, then **confirms** it " +
                "before the hold expires (`HOLD_DURATION_SECONDS`, default 600) or the seat is released for the next student.\n\n" +
                "* Student identity is the `X-Student-Id` header: a 17-digit registration number.\n" +
                "* Every error response has the same shape: `{ \"error\": \"SOME_CODE\", \"message\": \"human readable\" }`. " +
                "The `error` codes are listed under each response.\n" +
                "* `GET /health` is an operational probe and is not part of the student-facing contract."
        });

        // Summaries, remarks and per-status response descriptions come from the XML doc comments on the controllers.
        var xmlDocs = Path.Combine(
            Path.GetDirectoryName(typeof(SwaggerSetup).Assembly.Location) ?? AppContext.BaseDirectory,
            $"{typeof(SwaggerSetup).Assembly.GetName().Name}.xml");
        if (File.Exists(xmlDocs)) options.IncludeXmlComments(xmlDocs);

        options.OperationFilter<ContractOperationFilter>();
    }
}

/// <summary>
/// Makes the generated document match docs/API_CONTRACT.md where the C# signatures cannot say it:
/// X-Student-Id is mandatory (the parameter is <c>string?</c> so a missing header reaches the controller and gets the
/// standard 400 instead of the framework's own validation response), a request body is mandatory, and all bodies are JSON.
/// </summary>
internal sealed class ContractOperationFilter : IOperationFilter
{
    private const string Json = "application/json";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        foreach (var header in operation.Parameters.Where(p =>
                     p.In == ParameterLocation.Header && p.Name == SwaggerSetup.StudentIdHeader))
        {
            header.Required = true;
            header.Description = "The student's 17-digit registration number. Required; anything else is a 400 INVALID_STUDENT_ID.";
            header.Schema.Nullable = false;
            header.Schema.Pattern = SwaggerSetup.StudentIdPattern;
            header.Schema.Example = new OpenApiString("20152691234567890");
        }

        if (operation.RequestBody is { } body)
        {
            body.Required = true;
            KeepOnlyJson(body.Content);
        }

        foreach (var response in operation.Responses.Values)
            KeepOnlyJson(response.Content);
    }

    // The API only ever reads and writes application/json. Swashbuckle also lists text/plain and text/json for
    // controller actions that do not declare [Produces], which would suggest formats the API does not offer.
    private static void KeepOnlyJson(IDictionary<string, OpenApiMediaType> content)
    {
        foreach (var mediaType in content.Keys.Where(k => k != Json).ToList())
            content.Remove(mediaType);
    }
}
