using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies; 
using Microsoft.AspNetCore.HttpOverrides;
using System.Data.Common;
using Proposal.Models;
using Proposal.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var databaseProvider = builder.Configuration["Database:Provider"] ?? "SqlServer";
LogConnectionSummary(builder.Configuration, databaseProvider);


// Add services to the container.
builder.Services.AddControllersWithViews();

if (!builder.Environment.IsDevelopment())
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login"; // 如果沒登入被抓到，要被趕去哪個頁面
        options.AccessDeniedPath = "/Account/AccessDenied"; // 權限不足的畫面
        options.ExpireTimeSpan = TimeSpan.FromDays(1); // 登入狀態保持 1 天
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
    });

builder.Services.Configure<YouTubeSettings>(builder.Configuration.GetSection("YouTubeSettings"));
builder.Services.Configure<OpenRouterOptions>(builder.Configuration.GetSection("OpenRouter"));
builder.Services.AddSingleton<IPasswordHashService, Pbkdf2PasswordHashService>();
if (IsPostgresProvider(databaseProvider))
{
    builder.Services.AddSingleton<IPostgresConnectionFactory, PostgresConnectionFactory>();
    builder.Services.AddScoped<IUserAccountRepository, PostgresUserAccountRepository>();
    builder.Services.AddScoped<ICalculationHistoryRepository, PostgresCalculationHistoryRepository>();
    builder.Services.AddScoped<ICalculatorDataRepository, PostgresCalculatorDataRepository>();
    builder.Services.AddScoped<IEquipmentRepository, PostgresEquipmentRepository>();
    builder.Services.AddScoped<IAiRecommendationCache, PostgresAiRecommendationCache>();
    builder.Services.AddScoped<IAiRecommendationFavoriteService, PostgresAiRecommendationFavoriteService>();
    builder.Services.AddScoped<IAiKnowledgeBaseService, PostgresLolAramKnowledgeBaseService>();
    builder.Services.AddScoped<IUserActivityLogService, PostgresUserActivityLogService>();
    builder.Services.AddScoped<ILolAramGuideRepository, PostgresLolAramGuideRepository>();
    builder.Services.AddScoped<ILolAramAugmentRepository, PostgresLolAramAugmentRepository>();
}
else
{
    builder.Services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
    builder.Services.AddScoped<IUserAccountRepository, SqlUserAccountRepository>();
    builder.Services.AddScoped<ICalculationHistoryRepository, SqlCalculationHistoryRepository>();
    builder.Services.AddScoped<ICalculatorDataRepository, SqlCalculatorDataRepository>();
    builder.Services.AddScoped<IEquipmentRepository, SqlEquipmentRepository>();
    builder.Services.AddScoped<IAiRecommendationCache, SqlAiRecommendationCache>();
    builder.Services.AddScoped<IAiRecommendationFavoriteService, SqlAiRecommendationFavoriteService>();
    builder.Services.AddScoped<IAiKnowledgeBaseService, SqlLolAramKnowledgeBaseService>();
    builder.Services.AddScoped<IUserActivityLogService, SqlUserActivityLogService>();
    builder.Services.AddScoped<ILolAramGuideRepository, SqlLolAramGuideRepository>();
    builder.Services.AddScoped<ILolAramAugmentRepository, SqlLolAramAugmentRepository>();
}
builder.Services.AddHttpClient<IOpGgAramMayhemAugmentScraper, OpGgAramMayhemAugmentScraper>();
builder.Services.AddHttpClient<IOpGgAramMayhemChampionAugmentScraper, OpGgAramMayhemChampionAugmentScraper>();
builder.Services.AddHttpClient<IAiRecommendationService, OpenRouterRecommendationService>()
    .ConfigurePrimaryHttpMessageHandler(sp =>
    {
        var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<OpenRouterOptions>>()
            .CurrentValue;

        return new HttpClientHandler
        {
            UseProxy = !options.BypassSystemProxy
        };
    });


var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseForwardedHeaders();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    await next();
});
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapGet("/healthz", () => Results.Ok(new
{
    status = "ok",
    service = "proposal-aram-assistant"
}));

app.MapGet("/readyz", async (
    IServiceProvider services,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    var provider = configuration["Database:Provider"] ?? "SqlServer";

    try
    {
        await using var connection = CreateHealthConnection(services, provider);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "select 1;";
        command.CommandTimeout = 5;

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return string.Equals(Convert.ToString(result), "1", StringComparison.Ordinal)
            ? Results.Ok(new { status = "ready", databaseProvider = provider })
            : Results.Json(
                new { status = "not_ready", databaseProvider = provider },
                statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception ex) when (ex is DbException or InvalidOperationException or TimeoutException)
    {
        return Results.Json(
            new
            {
                status = "not_ready",
                databaseProvider = provider,
                error = ex.GetType().Name
            },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.UseStatusCodePagesWithReExecute("/Home/NotFoundPage"); ;

app.Run();

static void LogConnectionSummary(IConfiguration configuration, string provider)
{
    var defaultConnection = configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(defaultConnection))
    {
        Console.WriteLine($"Database provider={provider}; DefaultConnection is not configured.");
        return;
    }

    try
    {
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = defaultConnection
        };

        var endpoint =
            ReadConnectionValue(builder, "Data Source")
            ?? ReadConnectionValue(builder, "Server")
            ?? ReadConnectionValue(builder, "Host")
            ?? "(unknown)";

        var database =
            ReadConnectionValue(builder, "Initial Catalog")
            ?? ReadConnectionValue(builder, "Database")
            ?? "(unknown)";

        var security =
            ReadConnectionValue(builder, "Encrypt")
            ?? ReadConnectionValue(builder, "SSL Mode")
            ?? ReadConnectionValue(builder, "SslMode")
            ?? "(default)";

        Console.WriteLine(
            $"Database provider={provider}; endpoint={endpoint}; database={database}; ssl/encrypt={security}");
    }
    catch (ArgumentException)
    {
        Console.WriteLine($"Database provider={provider}; DefaultConnection is configured but could not be summarized.");
    }
}

static bool IsPostgresProvider(string? provider)
{
    return string.Equals(provider, "Postgres", StringComparison.OrdinalIgnoreCase)
        || string.Equals(provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase)
        || string.Equals(provider, "Supabase", StringComparison.OrdinalIgnoreCase);
}

static string? ReadConnectionValue(DbConnectionStringBuilder builder, string name)
{
    foreach (string key in builder.Keys)
    {
        if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToString(builder[key]);
        }
    }

    return null;
}

static DbConnection CreateHealthConnection(IServiceProvider services, string provider)
{
    if (IsPostgresProvider(provider))
    {
        return services.GetRequiredService<IPostgresConnectionFactory>().Create();
    }

    return services.GetRequiredService<ISqlConnectionFactory>().Create();
}
