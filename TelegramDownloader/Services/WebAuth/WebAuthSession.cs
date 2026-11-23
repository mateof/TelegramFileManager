using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace TelegramDownloader.Services.WebAuth
{
    /// <summary>
    /// Cookie session of the web UI: scheme registration, sign-in/out and the
    /// security stamp check that signs out every session when the password
    /// changes.
    /// </summary>
    public static class WebAuthSession
    {
        public const string Scheme = "TfmWeb";
        public const string CookieName = "tfm_session";
        public const string StampClaim = "tfm:stamp";
        /// <summary><c>password</c> (signed in with the password) or <c>open</c> (issued without password, open mode).</summary>
        public const string ModeClaim = "tfm:mode";

        /// <summary>"Remember me": persistent cookie.</summary>
        public static readonly TimeSpan RememberLifetime = TimeSpan.FromDays(30);
        /// <summary>Otherwise: browser-session cookie, idle timeout (sliding).</summary>
        public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);

        public static IServiceCollection AddWebAuthentication(this IServiceCollection services)
        {
            services.AddAuthentication(Scheme)
                .AddCookie(Scheme, o =>
                {
                    o.Cookie.Name = CookieName;
                    o.Cookie.HttpOnly = true;
                    o.Cookie.SameSite = SameSiteMode.Strict;
                    // Secure when the request is HTTPS (behind a trusted proxy the
                    // scheme comes from X-Forwarded-Proto).
                    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                    o.Cookie.Path = "/";
                    o.Cookie.IsEssential = true;
                    o.SlidingExpiration = true;
                    o.ExpireTimeSpan = RememberLifetime;

                    // Access is decided by WebAccessMiddleware; never redirect
                    // API calls to a login page.
                    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
                    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
                    o.Events.OnValidatePrincipal = async ctx =>
                    {
                        var auth = ctx.HttpContext.RequestServices.GetRequiredService<IWebAuthService>();
                        var stamp = ctx.Principal?.FindFirst(StampClaim)?.Value;
                        var mode = ctx.Principal?.FindFirst(ModeClaim)?.Value;
                        // Open-mode cookies stop working as soon as a password is set
                        // (the stamp changes too, this is belt and braces).
                        var valid = stamp != null && stamp == auth.SecurityStamp
                                    && (mode != "open" || !auth.PasswordSet);
                        if (!valid)
                        {
                            ctx.RejectPrincipal();
                            await ctx.HttpContext.SignOutAsync(Scheme);
                        }
                    };
                });
            return services;
        }

        public static Task SignInAsync(HttpContext context, IWebAuthService auth, bool remember, bool openMode = false)
        {
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "web"),
                new Claim(StampClaim, auth.SecurityStamp),
                new Claim(ModeClaim, openMode ? "open" : "password")
            }, Scheme);

            var now = DateTimeOffset.UtcNow;
            var props = new AuthenticationProperties
            {
                IsPersistent = remember,
                IssuedUtc = now,
                // The cookie handler keeps (expires - issued) when it slides, so
                // this is the idle timeout of each kind of session.
                ExpiresUtc = now + (remember ? RememberLifetime : SessionLifetime),
                AllowRefresh = true
            };
            return context.SignInAsync(Scheme, new ClaimsPrincipal(identity), props);
        }

        public static Task SignOutAsync(HttpContext context) => context.SignOutAsync(Scheme);

        public static bool IsSignedIn(HttpContext context) =>
            context.User?.Identities.Any(i => i.IsAuthenticated && i.AuthenticationType == Scheme) == true;
    }
}
