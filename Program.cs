using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Controllers;
using ServiceHub.Data;
using ServiceHub.Middleware;
using ServiceHub.Services;
using Microsoft.AspNetCore.DataProtection;
using System.Threading.Tasks;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

// ── Logging ───────────────────────────────────────────────────────────────
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddEventLog(settings =>
{
    settings.SourceName = "ServiceHub";
});

// ── Database ──────────────────────────────────────────────────────────────
var connectionString = builder.Configuration
    .GetConnectionString("ServiceHubContextConnection")
    ?? throw new InvalidOperationException("Connection string 'ServiceHubContextConnection' not found.");

builder.Services.AddDbContext<ServiceHubContext>(options =>
    options.UseSqlServer(connectionString));

// ── Identity (with roles) ─────────────────────────────────────────────────
builder.Services.AddDefaultIdentity<ApplicationUser>()
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ServiceHubContext>();

// ── Application Services ──────────────────────────────────────────────────
builder.Services.AddSingleton<TimeWindowService>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IMenuService, MenuService>();
builder.Services.AddScoped<IThemeService, ThemeService>();

// ── Email Service ─────────────────────────────────────────────────────────
// Real email sender is now active. Used by:
//   1. Forgot Password — sends reset link to user
//   2. DailyMachineReportService — sends machine report at configured time
// SMTP credentials are configured in appsettings.json under SmtpSettings.
// NOTE: Gmail requires an App Password if 2-Step Verification is enabled
//       on the sending account. A regular Gmail password will be rejected.
//       Generate one at: https://myaccount.google.com/apppasswords
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("SmtpSettings"));
builder.Services.AddTransient<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender, EmailSender>();

// ── Daily Machine Report (Background Service) ─────────────────────────────
builder.Services.Configure<DailyReportSettings>(builder.Configuration.GetSection("DailyMachineReport"));
builder.Services.AddHostedService<DailyMachineReportService>();

// ── HTTP Clients ──────────────────────────────────────────────────────────
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("EmployeeApi", client =>
{
    client.BaseAddress = new Uri("http://localhost:5000/");
    client.Timeout = TimeSpan.FromMinutes(5);
});

// ── Data Protection (IIS Compatible) ─────────────────────────────────────
var dataProtectionFolder = Path.Combine(
    builder.Environment.ContentRootPath, "DataProtection-Keys");

try
{
    Directory.CreateDirectory(dataProtectionFolder);
}
catch
{
    // Fallback if permissions are still not set on the main folder.
    // Permanently fix with: icacls "C:\inetpub\wwwroot\ServiceHub" /grant "IIS AppPool\YourPoolName":(OI)(CI)F /T
    dataProtectionFolder = Path.Combine(Path.GetTempPath(), "ServiceHub-Keys");
    Directory.CreateDirectory(dataProtectionFolder);
}

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionFolder))
    .SetApplicationName("ServiceHub")
    .SetDefaultKeyLifetime(TimeSpan.FromDays(90));
// DO NOT add .ProtectKeysWithDpapi() here - it breaks IIS app pool accounts.

// ── Cookie / Auth options ─────────────────────────────────────────────────
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Account/Login";
    options.LogoutPath = "/Identity/Account/Logout";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
    options.Cookie.HttpOnly = true;
    options.Cookie.Name = ".ServiceHub.Auth";
    options.SlidingExpiration = false;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);

    options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
    options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.None;

    options.Events = new Microsoft.AspNetCore.Authentication.Cookies
        .CookieAuthenticationEvents
    {
        OnSigningIn = ctx =>
        {
            if (ctx.Properties != null)
            {
                ctx.Properties.IsPersistent = false;
                ctx.Properties.ExpiresUtc = null;
            }
            return Task.CompletedTask;
        },

        OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.StatusCode = 401;
                return Task.CompletedTask;
            }
            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        },

        OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        }
    };
});

// ── Identity password policy ──────────────────────────────────────────────
builder.Services.Configure<IdentityOptions>(options =>
{
    options.Password.RequireUppercase = false;
});

// ── MVC ───────────────────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

// ── Build ─────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Seed roles on startup ─────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    try
    {
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "Admin", "HR", "User" })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Role seeding failed. Check DB connection.");
    }
}

// ── HTTP Pipeline ─────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// ── Menu-based URL-level RBAC guard ───────────────────────────────────────
app.UseMenuAuthorization();

// ── Unauthenticated redirect (safety net) ────────────────────────────────
app.Use(async (context, next) =>
{
    var isAuthenticated = context.User?.Identity?.IsAuthenticated == true;
    var path = context.Request.Path.Value ?? "/";

    if (!isAuthenticated &&
        !path.StartsWith("/Identity/", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWith("/Home/Error", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWith("/HR/TransferEmployee_API", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Redirect("/Identity/Account/Login");
        return;
    }

    await next();
});

// ── Routes ────────────────────────────────────────────────────────────────
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=AttendanceMachine}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();

app.Run();
