using FluentMigrator.Runner;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using MudBlazor.Services;
using TgSpam_PreFilterApi.Data.Repositories;
using TgSpam_PreFilterApi.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add MudBlazor services
builder.Services.AddMudServices();

// Configure Identity database path
builder.Services.Configure<Microsoft.Extensions.Configuration.ConfigurationManager>(options =>
{
    builder.Configuration["Identity:DatabasePath"] = Environment.GetEnvironmentVariable("IDENTITY_DATABASE_PATH") ?? "/data/identity.db";
});

// FluentMigrator for Identity database
var identityDbPath = builder.Configuration["Identity:DatabasePath"] ?? "/data/identity.db";
builder.Services
    .AddFluentMigratorCore()
    .ConfigureRunner(rb => rb
        .AddSQLite()
        .WithGlobalConnectionString($"Data Source={identityDbPath}")
        .ScanIn(typeof(TgSpam_PreFilterApi.Data.Migrations.IdentitySchema).Assembly).For.Migrations())
    .AddLogging(lb => lb.AddFluentMigratorConsole());

// Repositories
builder.Services.AddSingleton<UserRepository>();
builder.Services.AddSingleton<InviteRepository>();

// Cookie Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "TgSpam.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/access-denied";
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, ServerAuthenticationStateProvider>();

var app = builder.Build();

// Run Identity database migrations
using (var scope = app.Services.CreateScope())
{
    var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();

    if (runner.HasMigrationsToApplyUp())
    {
        app.Logger.LogInformation("Applying pending Identity migrations...");
        runner.MigrateUp();
    }
    else
    {
        app.Logger.LogInformation("Identity database schema is up to date");
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
