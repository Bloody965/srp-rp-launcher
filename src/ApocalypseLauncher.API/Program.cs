using System.Security.Cryptography;
using System.Text;
using ApocalypseLauncher.API;
using ApocalypseLauncher.API.Data;
using ApocalypseLauncher.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

var isDevelopment = builder.Environment.IsDevelopment();
var railwayPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(railwayPort))
{
    // Railway can route traffic to the dynamic PORT env variable.
    builder.WebHost.UseUrls($"http://0.0.0.0:{railwayPort}");
    Console.WriteLine($"[Startup] Using Railway PORT={railwayPort}");
}

// Читаем JWT Secret из разных источников (для совместимости с Railway)
var jwtSecret = builder.Configuration["Jwt:SecretKey"]
    ?? Environment.GetEnvironmentVariable("Jwt__SecretKey")
    ?? Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
    ?? Environment.GetEnvironmentVariable("JWT_SECRET");

Console.WriteLine($"[Startup] Environment: {builder.Environment.EnvironmentName}");
Console.WriteLine($"[Startup] JWT Secret configured: {!string.IsNullOrWhiteSpace(jwtSecret)}");
Console.WriteLine($"[Startup] JWT Secret length: {jwtSecret?.Length ?? 0}");
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

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            if (string.Equals(context.Request.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
                context.NoResult();
            return Task.CompletedTask;
        },
        OnTokenValidated = async context =>
        {
            try
            {
                var userIdClaim = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrWhiteSpace(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                {
                    context.Fail("Invalid token claims");
                    return;
                }

                var authHeader = context.Request.Headers.Authorization.ToString();
                var token = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    ? authHeader.Substring("Bearer ".Length).Trim()
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(token))
                {
                    context.Fail("Missing bearer token");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var jwt = context.HttpContext.RequestServices.GetRequiredService<JwtService>();
                var tokenHash = jwt.HashToken(token);

                var session = await db.LoginSessions
                    .FirstOrDefaultAsync(s => s.UserId == userId && s.Token == tokenHash && !s.IsRevoked);
                if (session == null || session.ExpiresAt < DateTime.UtcNow)
                {
                    context.Fail("Session is invalid or expired");
                    return;
                }

                var user = await db.Users.FindAsync(userId);
                if (user == null || !user.IsActive || user.IsBanned)
                {
                    context.Fail("Account is unavailable");
                }
            }
            catch
            {
                context.Fail("Authorization guard failed");
            }
        }
    };
});

builder.Services.AddAuthorization();

builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddSingleton<RateLimitService>();
builder.Services.AddSingleton<MinecraftServerService>();
builder.Services.AddSingleton<SkinValidationService>();
builder.Services.AddSingleton<YggdrasilSignatureService>();
builder.Services.AddScoped<UserIdentityConsistencyService>();
builder.Services.AddMemoryCache();

static byte[] DeriveWebHandoffHmacKey(string secret) =>
    SHA256.HashData(Encoding.UTF8.GetBytes(secret + "|srp-web-handoff-v1"));

builder.Services.AddSingleton(sp => new WebHandoffService(
    sp.GetRequiredService<IMemoryCache>(),
    DeriveWebHandoffHmacKey(jwtSecret)));

builder.Services.AddHttpClient();

builder.Services.AddControllers();

var allowedOrigins = CorsConfiguration.GetAllowedOrigins(builder.Configuration);
if (allowedOrigins.Length > 0)
{
    Console.WriteLine($"[CORS] Явные origin ({allowedOrigins.Length}): {string.Join("; ", allowedOrigins)}");
}

var corsAllowAny = string.Equals(
        builder.Configuration["Cors:AllowAnyOrigin"],
        "true",
        StringComparison.OrdinalIgnoreCase)
    || string.Equals(
        Environment.GetEnvironmentVariable("CORS_ALLOW_ANY_ORIGIN"),
        "true",
        StringComparison.OrdinalIgnoreCase);

builder.Services.AddCors(options =>
{
    options.AddPolicy("LauncherPolicy", policy =>
    {
        policy.WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
              .AllowAnyHeader()
              .SetPreflightMaxAge(TimeSpan.FromMinutes(10));

        policy.SetIsOriginAllowed(origin =>
            CorsConfiguration.IsOriginAllowed(origin, allowedOrigins, corsAllowAny, isDevelopment));
    });
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedProto
        | ForwardedHeaders.XForwardedHost;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddHttpContextAccessor();

var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append(
        "Permissions-Policy",
        "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()");
    if (!isDevelopment)
    {
        context.Response.Headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
    }

    await next();
});

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    Console.WriteLine("Database initialized successfully");

    // Добавляем колонку FileData если её нет (миграция схемы)
    try
    {
        // Проверяем и добавляем FileData в PlayerSkins
        db.Database.ExecuteSqlRaw(@"
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                              WHERE table_name='PlayerSkins' AND column_name='FileData') THEN
                    ALTER TABLE ""PlayerSkins"" ADD COLUMN ""FileData"" bytea NULL;
                    RAISE NOTICE 'Added FileData column to PlayerSkins';
                END IF;
            END $$;
        ");

        // Проверяем и добавляем FileData в PlayerCapes
        db.Database.ExecuteSqlRaw(@"
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                              WHERE table_name='PlayerCapes' AND column_name='FileData') THEN
                    ALTER TABLE ""PlayerCapes"" ADD COLUMN ""FileData"" bytea NULL;
                    RAISE NOTICE 'Added FileData column to PlayerCapes';
                END IF;
            END $$;
        ");

        // Колонки для принудительной смены пароля по админ-запросу
        db.Database.ExecuteSqlRaw(@"
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                              WHERE table_name='Users' AND column_name='IsAdminPasswordResetRequired') THEN
                    ALTER TABLE ""Users"" ADD COLUMN ""IsAdminPasswordResetRequired"" boolean NOT NULL DEFAULT false;
                END IF;

                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                              WHERE table_name='Users' AND column_name='AdminResetCodeHash') THEN
                    ALTER TABLE ""Users"" ADD COLUMN ""AdminResetCodeHash"" text NULL;
                END IF;

                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                              WHERE table_name='Users' AND column_name='AdminResetCodeExpiresAt') THEN
                    ALTER TABLE ""Users"" ADD COLUMN ""AdminResetCodeExpiresAt"" timestamp with time zone NULL;
                END IF;
            END $$;
        ");

        Console.WriteLine("Schema migration completed successfully");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Warning: Schema migration failed: {ex.Message}");
    }

    // Не удаляем скины/плащи с FileData=null при каждом старте: это приводило к потере скинов
    // после деплоя при сбоях миграции или временных NULL в БД.
}

app.UseForwardedHeaders();
if (!isDevelopment)
{
    app.UseHttpsRedirection();
}

// Явный preflight для /api/* до остального конвейера — устраняет «нет ACAO» при OPTIONS (JWT/порядок middleware).
app.Use(async (ctx, next) =>
{
    if (!HttpMethods.IsOptions(ctx.Request.Method))
    {
        await next();
        return;
    }

    var path = ctx.Request.Path.Value ?? "";
    if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }

    var origin = ctx.Request.Headers.Origin.ToString();
    if (!string.IsNullOrWhiteSpace(origin)
        && CorsConfiguration.IsOriginAllowed(origin, allowedOrigins, corsAllowAny, isDevelopment))
    {
        ctx.Response.Headers.Append("Access-Control-Allow-Origin", origin);
        ctx.Response.Headers.Append("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
        var acrh = ctx.Request.Headers.AccessControlRequestHeaders.ToString();
        if (!string.IsNullOrWhiteSpace(acrh))
            ctx.Response.Headers.Append("Access-Control-Allow-Headers", acrh);
        else
            ctx.Response.Headers.Append("Access-Control-Allow-Headers", "Authorization, Content-Type, Accept");
        ctx.Response.Headers.Append("Access-Control-Max-Age", "600");
    }

    ctx.Response.StatusCode = 204;
});

app.UseRouting();
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
