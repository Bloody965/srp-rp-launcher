using System.Text;
using ApocalypseLauncher.API.Data;
using ApocalypseLauncher.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var isDevelopment = builder.Environment.IsDevelopment();

// Читаем JWT Secret из разных источников (для совместимости с Railway)
var jwtSecret = builder.Configuration["Jwt:SecretKey"]
    ?? Environment.GetEnvironmentVariable("Jwt__SecretKey")
    ?? Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
    ?? Environment.GetEnvironmentVariable("JWT_SECRET");

Console.WriteLine($"[Startup] Environment: {builder.Environment.EnvironmentName}");
Console.WriteLine($"[Startup] JWT Secret configured: {!string.IsNullOrWhiteSpace(jwtSecret)}");
Console.WriteLine($"[Startup] JWT Secret length: {jwtSecret?.Length ?? 0}");
Console.WriteLine($"[Startup] JWT Secret first 20 chars: {(jwtSecret?.Length > 20 ? jwtSecret.Substring(0, 20) : jwtSecret ?? "null")}");
Console.WriteLine($"[Startup] Is placeholder: {jwtSecret == "CHANGE_THIS_TO_RANDOM_64_CHARACTERS_STRING_FOR_PRODUCTION"}");

if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret == "CHANGE_THIS_TO_RANDOM_64_CHARACTERS_STRING_FOR_PRODUCTION")
{
    if (!isDevelopment)
    {
        Console.WriteLine("[ERROR] JWT SecretKey is not configured for production.");
        Console.WriteLine("[ERROR] Checked variables: Jwt:SecretKey, Jwt__SecretKey, JWT_SECRET_KEY, JWT_SECRET");
        throw new InvalidOperationException("JWT SecretKey is not configured for production.");
    }

    jwtSecret = JwtService.GenerateSecureKey();
    Console.WriteLine("[Startup] Using temporary JWT secret for development environment.");
}

// Пробуем разные варианты переменных Railway PostgreSQL
var databaseUrl = builder.Configuration.GetConnectionString("DATABASE_URL")
    ?? Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? Environment.GetEnvironmentVariable("DATABASE_PRIVATE_URL")
    ?? Environment.GetEnvironmentVariable("PGDATABASE_URL");

Console.WriteLine($"[Startup] DATABASE_URL configured: {!string.IsNullOrEmpty(databaseUrl)}");
Console.WriteLine($"[Startup] DATABASE_URL length: {databaseUrl?.Length ?? 0}");
Console.WriteLine($"[Startup] DATABASE_URL full: {databaseUrl ?? "null"}");

if (!string.IsNullOrEmpty(databaseUrl))
{
    // Конвертируем PostgreSQL URI в Npgsql connection string
    if (databaseUrl.StartsWith("postgres://") || databaseUrl.StartsWith("postgresql://"))
    {
        try
        {
            var uri = new Uri(databaseUrl);
            var connectionString = $"Host={uri.Host};Port={uri.Port};Database={uri.AbsolutePath.TrimStart('/')};Username={uri.UserInfo.Split(':')[0]};Password={uri.UserInfo.Split(':')[1]};SSL Mode=Prefer;Trust Server Certificate=true";
            Console.WriteLine($"[Startup] Converted connection string: Host={uri.Host};Port={uri.Port};Database={uri.AbsolutePath.TrimStart('/')};Username={uri.UserInfo.Split(':')[0]};Password=***");
            databaseUrl = connectionString;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Startup] Error converting DATABASE_URL: {ex.Message}");
        }
    }

    Console.WriteLine("Using PostgreSQL database");
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(databaseUrl));
}
else
{
    Console.WriteLine("Using SQLite database");
    var sqliteConnection = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Data Source=apocalypse_launcher.db";
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlite(sqliteConnection));
}

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

builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddSingleton<RateLimitService>();
builder.Services.AddSingleton<MinecraftServerService>();
builder.Services.AddSingleton<SkinValidationService>();

builder.Services.AddControllers();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("LauncherPolicy", policy =>
    {
        policy.WithMethods("GET", "POST", "DELETE")
              .AllowAnyHeader();

        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins);
        }
    });
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    Console.WriteLine("Database initialized successfully");
}

app.UseForwardedHeaders();
if (!isDevelopment)
{
    app.UseHttpsRedirection();
}

app.UseCors("LauncherPolicy");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

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
Console.WriteLine("===========================================");

app.Run();
