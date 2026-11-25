using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<CargoHub.Api.Data.MySqlConnectionFactory>();
builder.Services.AddScoped(typeof(CargoHub.Api.Data.MySqlRepository<>));
builder.Services.AddScoped<CargoHub.Api.Services.WarehousesService>();
builder.Services.AddScoped<CargoHub.Api.Services.LocationsService>();
builder.Services.AddScoped<CargoHub.Api.Services.TransfersService>();
builder.Services.AddScoped<CargoHub.Api.Services.ShipmentsService>();
builder.Services.AddScoped<CargoHub.Api.Services.ItemsService>();
builder.Services.AddScoped<CargoHub.Api.Services.ItemLinesService>();
builder.Services.AddScoped<CargoHub.Api.Services.ItemGroupsService>();
builder.Services.AddScoped<CargoHub.Api.Services.ItemTypesService>();
builder.Services.AddScoped<CargoHub.Api.Services.ClientsService>();
builder.Services.AddScoped<CargoHub.Api.Services.SuppliersService>();
builder.Services.AddScoped<CargoHub.Api.Services.InventoriesService>();
builder.Services.AddScoped<CargoHub.Api.Services.OrdersService>();
builder.Services.AddScoped<CargoHub.Api.Services.AccountsService>();
builder.Services.Configure<CargoHub.Api.Monitoring.UptimeKumaOptions>(
    builder.Configuration.GetSection("UptimeKuma"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<CargoHub.Api.Monitoring.IUptimeKumaClient, CargoHub.Api.Monitoring.UptimeKumaClient>();
builder.Services.AddScoped<CargoHub.Api.Monitoring.UptimeService>();
builder.Services.AddSingleton<CargoHub.Api.Authentication.ApiUsersService>();

// Settings for blockade detection; the app refuses to start when a value is below 1.
builder
    .Services.AddOptions<CargoHub.Api.Settings.BlockadeDetectionOptions>()
    .Bind(builder.Configuration.GetSection(CargoHub.Api.Settings.BlockadeDetectionOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Mail settings; production points to Resend, local runs and CI to Mailpit.
builder
    .Services.AddOptions<CargoHub.Api.Settings.EmailOptions>()
    .Bind(builder.Configuration.GetSection(CargoHub.Api.Settings.EmailOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<CargoHub.Api.Services.IEmailService, CargoHub.Api.Services.EmailService>();
builder
    .Services.AddHealthChecks()
    .AddCheck<CargoHub.Api.Data.MySqlHealthCheck>("mysql", timeout: TimeSpan.FromSeconds(4));

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = CargoHub.Api.Errors.ApiProblemDetails.Customize
);

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(
    "v2",
    options =>
    {
        options.AddDocumentTransformer<CargoHub.Api.Authentication.ApiKeyDocumentTransformer>();
        options.AddSchemaTransformer<CargoHub.Api.OpenApi.ServerSetFieldsSchemaTransformer>();
    }
);

var app = builder.Build();

// Return JSON errors for unhandled exceptions and otherwise empty error responses.
app.UseExceptionHandler(errorApp =>
    errorApp.Run(context =>
        CargoHub.Api.Errors.ApiProblemDetails.WriteAsync(
            context,
            StatusCodes.Status500InternalServerError
        )
    )
);
app.UseStatusCodePages(context =>
    CargoHub.Api.Errors.ApiProblemDetails.WriteAsync(
        context.HttpContext,
        context.HttpContext.Response.StatusCode
    )
);
app.UseRouting();

// Endpoint overview: /openapi/v2.json for tools, /docs for people.
// Both are outside /api, so they need no API key; calling an endpoint still does.
app.MapOpenApi();
app.MapScalarApiReference(
    "/docs",
    options =>
    {
        options
            .WithTitle("CargoHub API")
            .AddDocument("v2")
            .AddPreferredSecuritySchemes(CargoHub.Api.Authentication.ApiKeyMiddleware.HeaderName);
        options.HideModels = true;
    }
);

app.UseHttpsRedirection();

app.UseMiddleware<CargoHub.Api.Authentication.ApiKeyMiddleware>();

app.UseAuthorization();

app.MapControllers();

// For uptime monitors and the Docker healthcheck: 200 "Healthy", or 503 "Unhealthy" when
// MySQL is down. Outside /api, so it needs no API key.
app.MapHealthChecks("/health");

app.Run();

// Expose the API entry point to WebApplicationFactory in integration tests.
public partial class Program { }