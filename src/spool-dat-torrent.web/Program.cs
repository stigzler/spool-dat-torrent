using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.Data;
using SpoolDatTorrent.Core.Interfaces;
using SpoolDatTorrent.Core.Services;
using SpoolDatTorrent.Web.Components;
using System.Security.Claims;

namespace SpoolDatTorrent.Web
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            // Add MudBlazor services
            builder.Services.AddMudServices(config =>
            {
                config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
                config.SnackbarConfiguration.PreventDuplicates = false;
                config.SnackbarConfiguration.NewestOnTop = true;
                config.SnackbarConfiguration.ShowCloseIcon = true;
                config.SnackbarConfiguration.VisibleStateDuration = 5000;
            });

            // Load the shared config.json (same file the CLI uses) and expose it via DI.
            var settings = SettingsManager.LoadSettings();
            builder.Services.AddSingleton(settings);
            builder.Services.AddSingleton<IOptions<GlobalSpoolSettings>>(Options.Create(settings));

            // Core services (shared with the CLI). The engine is NOT started as a hosted
            // service yet — that arrives with the live-progress milestone.
            builder.Services.AddDbContext<SpoolDbContext>(options => options.UseSqlite("DataSource=spooldattorrent.db"));
            builder.Services.AddHttpClient();
            builder.Services.AddSingleton<IBitTorrentClientFactory, BitTorrentClientFactory>();
            builder.Services.AddTransient<IDatParserService, LogiqxDatParserService>();
            builder.Services.AddTransient<SpoolDatTorrent.Core.Commands.DeleteServerProfileCommand>();
            builder.Services.AddTransient<SpoolDatTorrent.Core.Commands.AddServerProfileCommand>();

            // Cookie authentication (single admin).
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.LoginPath = "/login";
                    options.Cookie.Name = "sdt_auth";
                });
            builder.Services.AddAuthorization();
            builder.Services.AddCascadingAuthenticationState();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error", createScopeForErrors: true);
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseAntiforgery();

            app.UseAuthentication();
            app.UseAuthorization();

            // Login/logout endpoints. Login compares the submitted password against the
            // AdminPassword in config.json and signs the user in via a cookie.
            // NOTE: these use /auth/* paths (not /login) to avoid colliding with the
            // Login.razor @page "/login" route, which also matches POST in Blazor.
            app.MapPost("/auth/login", async (HttpContext ctx, IOptions<GlobalSpoolSettings> opts) =>
            {
                var form = await ctx.Request.ReadFormAsync();
                var password = form["password"].ToString();

                if (!string.IsNullOrWhiteSpace(password) && password == opts.Value.AdminPassword)
                {
                    var claims = new List<Claim> { new(ClaimTypes.Name, "admin") };
                    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
                    return Results.Redirect("/");
                }

                return Results.Redirect("/login?error=1");
            }).DisableAntiforgery();

            app.MapGet("/auth/logout", async (HttpContext ctx) =>
            {
                await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.Redirect("/login");
            });

            app.MapStaticAssets();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.Run();
        }
    }
}
