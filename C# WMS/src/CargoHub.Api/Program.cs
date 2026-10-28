var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<CargoHub.Api.Data.MySqlConnectionFactory>();
builder.Services.AddScoped(typeof(CargoHub.Api.Data.MySqlRepository<>));
builder.Services.AddScoped<CargoHub.Api.Services.WarehousesService>();
builder.Services.AddScoped<CargoHub.Api.Services.LocationsService>();
builder.Services.AddScoped<CargoHub.Api.Services.ShipmentsService>();
builder.Services.AddScoped<CargoHub.Api.Services.ItemsService>();
builder.Services.AddScoped<CargoHub.Api.Services.ClientsService>();
builder.Services.AddScoped<CargoHub.Api.Services.SuppliersService>();
builder.Services.AddScoped<CargoHub.Api.Services.OrdersService>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<CargoHub.Api.Authentication.ApiUsersService>();

// Add services to the container.

builder.Services.AddControllers();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseMiddleware<CargoHub.Api.Authentication.ApiKeyMiddleware>();

app.UseAuthorization();

app.MapControllers();

app.Run();
