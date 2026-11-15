using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace CalixHelper.Api.Features.Auth;

/// <summary>
/// Temporary login: one user from configuration (section "Auth").
/// To be replaced by real authentication (user store / identity provider).
/// </summary>
public sealed class AuthOptions
{
    public const string Section = "Auth";

    public List<AuthUser> Users { get; set; } = [];
}

public sealed class AuthUser
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed record LoginRequest(string? Username, string? Password);
public sealed record UserDto(string Username);

public static class AuthEndpoints
{
    public static IServiceCollection AddCalixAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.Section));

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = "calix.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Strict; // the cookie is never sent with cross-site requests (CSRF)
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromHours(8);
                o.SlidingExpiration = true;
                // An API answers 401/403 instead of redirecting to a login page.
                o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
            });
        services.AddAuthorization();
        return services;
    }

    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", LoginAsync).DisableAntiforgery();
        group.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).DisableAntiforgery();
        group.MapGet("/me", (ClaimsPrincipal user) =>
            user.Identity?.IsAuthenticated == true ? Results.Ok(new UserDto(user.Identity.Name!)) : Results.Unauthorized());

        return app;
    }

    private static async Task<IResult> LoginAsync(LoginRequest body, HttpContext ctx, IOptions<AuthOptions> options, ILoggerFactory loggers)
    {
        var username = body.Username?.Trim() ?? "";
        var password = body.Password ?? "";
        var user = options.Value.Users.FirstOrDefault(u =>
            string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase) && FixedTimeEquals(u.Password, password));

        if (user is null)
        {
            loggers.CreateLogger("Auth").LogWarning("Neuspjela prijava za korisnika '{User}'", username);
            await Task.Delay(500); // slows down password guessing
            return Results.Problem(title: "Pogrešno korisničko ime ili lozinka.", statusCode: StatusCodes.Status401Unauthorized);
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, user.Username)], CookieAuthenticationDefaults.AuthenticationScheme);
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Results.Ok(new UserDto(user.Username));
    }

    private static bool FixedTimeEquals(string expected, string actual) =>
        expected.Length > 0 &&
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(actual)));
}
