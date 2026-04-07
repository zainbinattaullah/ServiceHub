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

// ── Database ─────────────────────────────────────────────────────────────
var connectionString = builder.Configuration
    .GetConnectionString("ServiceHubContextConnection")
    ?? throw new InvalidOperationException("Connection string not found.");

builder.Services.AddDbContext<ServiceHubContext>(options =>
    options.UseSqlServer(connectionString));

// ── Identity (with roles) ─────────────────────────────────────────────────
builder.Services.AddDefaultIdentity<ApplicationUser>()
    .AddRoles<IdentityRole>()                   // ← Enables Role Manager
    .AddEntityFrameworkStores<ServiceHubContext>();

// ── Application Services ──────────────────────────────────────────────────
builder.Services.AddSingleton<TimeWindowService>();
builder.Services.AddMemoryCache();                          // needed by MenuService
builder.Services.AddScoped<IMenuService, MenuService>();
builder.Services.AddScoped<IThemeService, ThemeService>();

// ── Email Service ─────────────────────────────────────────────────────────
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("SmtpSettings"));
builder.Services.AddTransient<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender, EmailSender>();

// ── Daily Machine Report (Background Service) ────────────────────────────
builder.Services.Configure<DailyReportSettings>(builder.Configuration.GetSection("DailyMachineReport"));
builder.Services.AddHostedService<DailyMachineReportService>();

// ── HTTP Clients ──────────────────────────────────────────────────────────
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("EmployeeApi", client =>
{
    client.BaseAddress = new Uri("http://localhost:5000/");
    client.Timeout = TimeSpan.FromMinutes(5);
});

// ── Data Protection ───────────────────────────────────────────────────────
var dataProtectionFolder = Path.Combine(
    builder.Environment.ContentRootPath, "DataProtection-Keys");

if (builder.Environment.IsDevelopment())
{
    try
    {
        var reset = Environment.GetEnvironmentVariable("RESET_DP_KEYS");
        if (!string.IsNullOrEmpty(reset) && reset == "1" &&
            Directory.Exists(dataProtectionFolder))
            Directory.Delete(dataProtectionFolder, true);
    }
    catch { }
}

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionFolder))
    .SetApplicationName("ServiceHub")
    .ProtectKeysWithDpapi();

// ── Cookie / Auth options ─────────────────────────────────────────────────
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath        = "/Identity/Account/Login";
    options.LogoutPath       = "/Identity/Account/Logout";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
    options.Cookie.HttpOnly  = true;
    options.SlidingExpiration = false;
    options.ExpireTimeSpan   = TimeSpan.FromMinutes(30);

    options.Events = new Microsoft.AspNetCore.Authentication.Cookies
        .CookieAuthenticationEvents
    {
        OnSigningIn = ctx =>
        {
            if (ctx.Properties != null)
            {
                ctx.Properties.IsPersistent = false;
                ctx.Properties.ExpiresUtc   = null;
            }
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
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "Admin", "HR", "User" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }
}

// ── HTTP Pipeline ─────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Home/Error");

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// ── Menu-based URL-level RBAC guard ──────────────────────────────────────
// Must come AFTER UseAuthorization so ClaimsPrincipal is populated.
app.UseMenuAuthorization();

// ── Route registration (unauthenticated redirect kept as safety net) ───────
app.Use(async (context, next) =>
{
    var isAuthenticated = context.User?.Identity?.IsAuthenticated == true;
    var path = context.Request.Path.Value ?? "/";

    if (!isAuthenticated &&
        !path.StartsWith("/Identity/Account/Login",  StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWith("/Identity/Account/Logout",  StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWith("/Identity/Account/ForgotPassword", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWith("/Identity/Account/ResetPassword", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWith("/HR/TransferEmployee_API",  StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Redirect("/Identity/Account/Login");
        return;
    }
    await next();
});

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=AttendanceMachine}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();

app.Run();
