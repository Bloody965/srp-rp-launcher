using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using ApocalypseLauncher.API.Data;
using ApocalypseLauncher.API.Services;

var builder = WebApplication.CreateBuilder(args);

// Генерация секретного ключа при первом запуске (сохраните его в appsettings.json!)
var jwtSecret = builder.Configuration["Jwt:SecretKey"];
if (string.IsNullOrEmpty(jwtSecret))
{
    jwtSecret = JwtService.GenerateSecureKey();
    Console.WriteLine("===========================================");
    Console.WriteLine("ВАЖНО! Сохраните этот JWT секретный ключ:");
    Console.WriteLine(jwtSecret);
    Console.WriteLine("Добавьте его в appsettings.json:");
    Console.WriteLine("\"Jwt\": { \"SecretKey\": \"" + jwtSecret + "\" }");
    Console.WriteLine("===========================================");
}

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Data Source=apocalypse_launcher.db"));

// JWT Authentication
var key = Encoding.UTF8.GetBytes(jwtSecret);
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "ApocalypseLauncher.API",
        ValidateAudience = true,
        ValidAudience = builder.Configuration["Jwt:Audience"] ?? "ApocalypseLauncher.Client",
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();

// Services
builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddSingleton<RateLimitService>();

// Controllers
builder.Services.AddControllers();

// CORS (для лаунчера)
builder.Services.AddCors(options =>
{
    options.AddPolicy("LauncherPolicy", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Создание базы данных при первом запуске
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    Console.WriteLine("Database initialized successfully");
}

// Configure the HTTP request pipeline

// app.UseHttpsRedirection(); // Отключено для локальной разработки
app.UseCors("LauncherPolicy");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Health check endpoint
app.MapGet("/api/health", () => new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    version = "1.0.0"
});

Console.WriteLine("===========================================");
Console.WriteLine("Apocalypse Launcher API Server");
Console.WriteLine("===========================================");
Console.WriteLine($"Environment: {app.Environment.EnvironmentName}");
Console.WriteLine($"Swagger UI: https://localhost:7000/swagger");
Console.WriteLine($"Health Check: https://localhost:7000/api/health");
Console.WriteLine("===========================================");

app.Run();
