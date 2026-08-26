using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using SpoolDatTorrent.Core.Configuration;
using SpoolDatTorrent.Core.Data;
using SpoolDatTorrent.Core.Interfaces;
using SpoolDatTorrent.Core.Progress;
using SpoolDatTorrent.Core.Services;
using SpoolDatTorrent.Web.Components;
using SpoolDatTorrent.Web.Services;
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
            builder.Services.AddHttpContextAccessor();

            // Raise the form/file-upload limits. Torrent files for large 1G1R sets can be
            // several MB, so allow generous per-file and total sizes.
            builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = 2L * 1024 * 1024 * 1024; // 2 GB total
                options.ValueLengthLimit = int.MaxValue;
            });

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

            // Core services (shared with the CLI). The engine runs as a hosted service so
            // live progress snapshots are available to the Streams page.
            builder.Services.AddDbContext<SpoolDbContext>(options => options.UseSqlite("DataSource=spooldattorrent.db"));
            builder.Services.AddHttpClient();
            builder.Services.AddSingleton<IBitTorrentClientFactory, BitTorrentClientFactory>();
            builder.Services.AddSingleton<IDatParserService, LogiqxDatParserService>();
            builder.Services.AddSingleton<InMemoryProgressStore>();
            builder.Services.AddSingleton<ISpoolingProgressReporter>(sp => sp.GetRequiredService<InMemoryProgressStore>());
            builder.Services.AddSingleton<SpoolingEngine>();
            builder.Services.AddHostedService<SpoolEngineHostedService>();
            builder.Services.AddTransient<SpoolDatTorrent.Core.Commands.DeleteServerProfileCommand>();
            builder.Services.AddTransient<SpoolDatTorrent.Core.Commands.AddServerProfileCommand>();
            builder.Services.AddTransient<SpoolDatTorrent.Core.Commands.AddStreamCommand>();
            builder.Services.AddTransient<SpoolDatTorrent.Core.Commands.CancelStreamCommand>();
            builder.Services.AddTransient<SpoolDatTorrent.Core.Commands.SetStreamStatusCommand>();
            builder.Services.AddTransient<SpoolDatTorrent.Core.Commands.RetryStreamCommand>();
            builder.Services.AddTransient<SpoolDatTorrent.Core.Commands.ListStreamsCommand>();
            builder.Services.AddTransient<SpoolDatTorrent.Core.Commands.PauseAllStreamsCommand>();
            builder.Services.AddTransient<SpoolDatTorrent.Core.Commands.ResumeAllStreamsCommand>();
            builder.Services.AddSingleton<GlobalPauseService>();

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

            // Apply any pending EF migrations on startup so existing databases are upgraded
            // seamlessly when a new schema version ships (no manual steps for the user).
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SpoolDatTorrent.Core.Data.SpoolDbContext>();
                db.Database.Migrate();
            }

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
            // admin password and signs the user in via a cookie.
            // The password comes from the SDT_ADMIN_PASSWORD environment variable (set in a
            // compose file / Docker secret).
            // NOTE: these use /auth/* paths (not /login) to avoid colliding with the
            // Login.razor @page "/login" route, which also matches POST in Blazor.
            app.MapPost("/auth/login", async (HttpContext ctx) =>
            {
                var form = await ctx.Request.ReadFormAsync();
                var password = form["password"].ToString();

                var expected = Environment.GetEnvironmentVariable("SDT_ADMIN_PASSWORD") ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(password) && password == expected)
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
