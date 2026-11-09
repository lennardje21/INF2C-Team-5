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
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<CargoHub.Api.Authentication.ApiUsersService>();

// Add services to the container.

builder.Services.AddControllers();

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

app.Run();
