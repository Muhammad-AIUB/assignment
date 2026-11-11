using Api.BackgroundServices;
using Api.Cors;
using Api.Dtos;
using Api.Middleware;
using Api.Services;
using FluentValidation;
using FluentValidation.AspNetCore;
using Infrastructure;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        // {Properties:j} prints the structured properties, which is where the per-request CorrelationId shows up.
        .WriteTo.Console(outputTemplate:
            "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"));

    // Postgres connection string: ConnectionStrings:Postgres (appsettings) or the
    // ConnectionStrings__Postgres env var. Hold duration: HOLD_DURATION_SECONDS (default 600).
    builder.Services.AddInfrastructure(builder.Configuration);

    builder.Services.AddControllers()
        .ConfigureApiBehaviorOptions(o =>
        {
            // Bare 4xx results (415 from content negotiation, ...) are left empty here instead of being turned into
            // ProblemDetails, so the status-code handler below can give them the standard error shape.
            o.SuppressMapClientErrors = true;

            // All validation / binding failures use the standard error shape.
            o.InvalidModelStateResponseFactory = ctx =>
            {
                var message = string.Join("; ", ctx.ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Invalid request." : e.ErrorMessage)
                    .Distinct());
                return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(
                    new ErrorResponse("VALIDATION_ERROR", message));
            };
        });
    // Which browser origins may call the API: CORS_ALLOWED_ORIGINS (default: the local frontend dev servers).
    // A bad value throws here, so the host stops at startup (logged as Fatal below) instead of refusing every browser later.
    var corsOptions = ApiCorsOptions.FromConfiguration(builder.Configuration);
    builder.Services.AddApiCors(corsOptions.AllowedOrigins);
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddScoped<IClassService, ClassService>();
    builder.Services.AddScoped<IHoldService, HoldService>();
    builder.Services.AddScoped<IHealthService, HealthService>();
    builder.Services.AddScoped<IHoldExpirySweeper, HoldExpirySweeper>();
    builder.Services.AddHostedService<HoldExpiryWorker>();
    builder.Services.AddFluentValidationAutoValidation();
    builder.Services.AddValidatorsFromAssemblyContaining<Program>();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(Api.Swagger.SwaggerSetup.Configure);

    var app = builder.Build();

    if (corsOptions.AllowedOrigins.Count == 0)
        app.Logger.LogInformation("CORS is off: {Setting} is blank, so no browser origin may call this API", ApiCorsOptions.ConfigKey);
    else
        app.Logger.LogInformation("CORS allows browser origins {AllowedOrigins}", corsOptions.AllowedOrigins);

    // Shown at startup so an operator can see what each instance may take from PostgreSQL (instances x this must stay below its max_connections).
    app.Logger.LogInformation("Database pool allows at most {MaxPoolSize} connections per instance",
        PostgresConnectionString.WithDefaultPoolLimit(builder.Configuration.GetConnectionString("Postgres")!).MaxPoolSize);

    // Apply pending migrations on startup.
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    // Order matters. The correlation id goes first so request logging and everything inside it carries it; the exception
    // handler sits inside request logging so the request log records the status the client actually received. CORS sits
    // between the two: inside request logging, so preflights are logged too, and outside the exception handler, so a
    // preflight never reaches a controller.
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseCors();
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    // A body labelled with a charset other than UTF-8 (or with a Content-Type that cannot be parsed) is refused with 415
    // before model binding, where it used to throw inside MVC's decoder and reach the handler above as UNEXPECTED_ERROR.
    app.UseMiddleware<RequestContentTypeMiddleware>();

    // 4xx the framework produces with no body of its own (unknown route 404, wrong method 405, wrong content type 415)
    // get the standard error shape too. Responses that already have a body (every controller error) are left alone.
    app.UseStatusCodePages(context =>
    {
        var status = context.HttpContext.Response.StatusCode;
        if (status is < 400 or > 499) return Task.CompletedTask;
        var (code, message) = HttpErrors.ForStatus(status);
        return ErrorResponseWriter.WriteAsync(context.HttpContext, status, code, message);
    });

    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapControllers();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
