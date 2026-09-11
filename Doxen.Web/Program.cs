using Doxen.Data;
using Doxen.Web.Data;
using Doxen.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Doxen")
    ?? throw new InvalidOperationException("Не задана строка подключения ConnectionStrings:Doxen.");

builder.Services.AddRazorPages(o =>
{
    // Всё под /admin закрыто политикой на уровне папки, а не проверками
    // в каждой странице по отдельности (05-screens.md).
    o.Conventions.AuthorizeFolder("/Admin", "AdminOnly");
});

builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
});

var templateCacheOptions = new TemplateCacheOptions
{
    BudgetBytes = builder.Configuration.GetValue("Doxen:TemplateCacheSizeBytes", 2_000_000_000L),
    SoftLimitPercent = builder.Configuration.GetValue("Doxen:SoftLimitPercent", 75),
    HardLimitPercent = builder.Configuration.GetValue("Doxen:HardLimitPercent", 90),
    FreeUserWaitSeconds = builder.Configuration.GetValue("Doxen:FreeUserWaitSeconds", 20),
    MaxQueuedFreeUsers = builder.Configuration.GetValue("Doxen:MaxQueuedFreeUsers", 50),
    LifetimeHours = builder.Configuration.GetValue("Doxen:TemplateCacheLifetimeHours", 2),
};

builder.Services.AddMemoryCache(o => o.SizeLimit = templateCacheOptions.BudgetBytes);
builder.Services.AddSingleton(templateCacheOptions);
builder.Services.AddSingleton<TemplateCache>();
builder.Services.AddSingleton<GenerateSessionStore>();
builder.Services.AddSingleton<AnonymousRateLimiter>();

// Состояние экрана заполнения — в сессии, шаблон отдельно в TemplateCache.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.IdleTimeout = TimeSpan.FromHours(2);
});

// EF Core обслуживает только таблицы Identity — см. AuthDbContext.
builder.Services.AddDbContext<AuthDbContext>(o => o.UseNpgsql(connectionString));

builder.Services.AddIdentity<DoxenUser, IdentityRole<long>>(o =>
    {
        o.Password.RequiredLength = 10;
        o.Password.RequireNonAlphanumeric = false; // длина важнее классов символов
        o.User.RequireUniqueEmail = true;
        o.SignIn.RequireConfirmedEmail = false; // почта не подключена, см. 07-build-plan.md
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AuthDbContext>()
    .AddDefaultTokenProviders();

// Без этого краденая cookie продолжает работать после смены пароля.
builder.Services.Configure<SecurityStampValidatorOptions>(
    o => o.ValidationInterval = TimeSpan.FromMinutes(5));

builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.ExpireTimeSpan = TimeSpan.FromDays(14);
    o.SlidingExpiration = true;
});

// Бизнес-таблицы Doxen — только чистый Npgsql, без ORM.
builder.Services.AddSingleton(new Db(connectionString));
builder.Services.AddSingleton<PlanRepository>();
builder.Services.AddSingleton<UserProfileRepository>();
builder.Services.AddScoped<CurrentPlanResolver>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    // Сначала EF Core (таблицы Identity), потом самописные миграции —
    // два непересекающихся набора таблиц, порядок важен только при
    // первом старте на чистой базе.
    var authContext = services.GetRequiredService<AuthDbContext>();
    await authContext.Database.MigrateAsync();

    await Migrations.ApplyAsync(connectionString);

    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("AdminSeeder");
    await AdminSeeder.SeedAsync(services, logger);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();

app.UseRouting();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.Run();
