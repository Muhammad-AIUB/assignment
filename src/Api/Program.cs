using Api.Dtos;
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
        .WriteTo.Console());

    // Postgres connection string: ConnectionStrings:Postgres (appsettings) or the
    // ConnectionStrings__Postgres env var. Hold duration: HOLD_DURATION_SECONDS (default 600).
    builder.Services.AddInfrastructure(builder.Configuration);

    builder.Services.AddControllers()
        .ConfigureApiBehaviorOptions(o =>
        {
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
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddScoped<IClassService, ClassService>();
    builder.Services.AddFluentValidationAutoValidation();
    builder.Services.AddValidatorsFromAssemblyContaining<Program>();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    var app = builder.Build();

    // Apply pending migrations on startup.
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    app.UseSerilogRequestLogging();
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
