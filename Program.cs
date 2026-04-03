using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Controllers;
using ServiceHub.Data;
using Microsoft.AspNetCore.DataProtection;
using System.Threading.Tasks;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("ServiceHubContextConnection") ?? throw new InvalidOperationException("Connection string 'ServiceHubContextConnection' not found.");

builder.Services.AddDbContext<ServiceHubContext>(options => options.UseSqlServer(connectionString));

builder.Services.AddDefaultIdentity<ApplicationUser>().AddEntityFrameworkStores<ServiceHubContext>();
builder.Services.AddSingleton<TimeWindowService>();
builder.Services.AddHttpClient();
// Register a named/typed HttpClient for employee registration calls
builder.Services.AddHttpClient("EmployeeApi", client =>
{
    client.BaseAddress = new Uri("http://localhost:5000/");
    client.Timeout = TimeSpan.FromMinutes(5);
});

// Configure Data Protection keys — store under project folder so you can control them in development.
// NOTE: deleting keys on every startup invalidates existing cookies/antiforgery tokens and causes the
// "key not found" / "antiforgery token could not be decrypted" errors. To avoid that, do NOT remove
// the keys automatically. If you need to reset keys during development, set the environment variable
// RESET_DP_KEYS=1 before launching the app.
var dataProtectionFolder = Path.Combine(builder.Environment.ContentRootPath, "DataProtection-Keys");
if (builder.Environment.IsDevelopment())
{
    try
    {
        // Only remove keys if an explicit env var is set to avoid accidental loss of keys
        var reset = Environment.GetEnvironmentVariable("RESET_DP_KEYS");
        if (!string.IsNullOrEmpty(reset) && reset == "1" && Directory.Exists(dataProtectionFolder))
        {
            Directory.Delete(dataProtectionFolder, true);
        }
    }
    catch { }
}

// Persist keys to file system and protect them using DPAPI (Windows). Also set an explicit application name
// so keys are shared consistently if you run multiple app instances with the same name.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionFolder))
    .SetApplicationName("ServiceHub")
    .ProtectKeysWithDpapi();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = $"/Identity/Account/Login"; 
    options.LogoutPath = $"/Identity/Account/Logout";
    options.AccessDeniedPath = $"/Identity/Account/AccessDenied";
    options.Cookie.HttpOnly = true;
    // Prefer session cookies (not persistent) so closing the browser clears authentication.
    options.SlidingExpiration = false;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);

    // Force issued cookies to be non-persistent regardless of 'Remember me' so closing the browser clears the session.
    options.Events = new Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationEvents
    {
        OnSigningIn = context =>
        {
            if (context.Properties != null)
            {
                context.Properties.IsPersistent = false; // session cookie
                context.Properties.ExpiresUtc = null;
            }
            return Task.CompletedTask;
        }
    };
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
});

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

builder.Services.Configure<IdentityOptions>(
    options => {
        options.Password.RequireUppercase = false;
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();

app.UseAuthorization();

// Add area routing
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=AttendanceMachine}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Use(async (context, next) =>
{
    // Allow users to access Login and Register pages
    var isAuthenticated = context.User?.Identity?.IsAuthenticated == true;

    if (!isAuthenticated &&
        !context.Request.Path.StartsWithSegments("/Identity/Account/Login") &&
        !context.Request.Path.StartsWithSegments("/Identity/Account/Register") &&
        !context.Request.Path.StartsWithSegments("/Identity/Account/Logout"))
    {
        // Check if the request is for an API endpoint
        if (!context.Request.Path.StartsWithSegments("/HR/TransferEmployee_API"))
        {
            context.Response.Redirect("/Identity/Account/Login");
            return;
        }
    }
    await next();
});

app.MapRazorPages();

app.Run();
