using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace ModernForms.Api.Auth;

/// <summary>
/// Auto-switching authentication: "Development" issues local JWTs from
/// configured demo users (same shape as the legacy employee keys), while
/// "AzureAd" validates tokens from the SSO tenant. Modes never both run.
/// </summary>
public static class AuthOptions
{
    /// <summary>Simplified demo-issuer configuration section.</summary>
    public sealed class Development
    {
        public string Issuer { get; set; } = "modernforms.dev";

        public string Audience { get; set; } = "modernforms.api";

        public int TokenLifetimeMinutes { get; set; } = 120;

        /// <summary>Base64 HMAC key — demo-only; production uses certificate SSO.</summary>
        public string SigningKeyBase64 { get; set; } = string.Empty;

        public Dictionary<string, DemoUser> Users { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class DemoUser
    {
        /// <summary>The legacy employee_key this identity represents (usr_*).</summary>
        public string ExternalId { get; set; } = string.Empty;

        public string[] Roles { get; set; } = Array.Empty<string>();
    }

    public static void ConfigureAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        var mode = configuration["Auth:Mode"] ?? "Development";

        if (string.Equals(mode, "Development", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IDevTokenIssuer, DevelopmentJwtTokenIssuer>();

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    var dev = configuration.GetSection("Auth:Development").Get<Development>() ?? new Development();

                    options.MapInboundClaims = false; // keep raw claim types: oid = employee key, roles = role bag

                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = dev.Issuer,
                        ValidateAudience = true,
                        ValidAudience = dev.Audience,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(DecodeSigningKey(dev.SigningKeyBase64)),
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.FromMinutes(2),
                        RequireExpirationTime = true,
                        NameClaimType = "unique_name",
                        RoleClaimType = "roles"
                    };
                });
        }
        else
        {
            // SSO path (Azure AD): validated by the platform middleware; demo wiring noted in docs.
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer();
        }

        services.AddAuthorization();
    }

    private static byte[] DecodeSigningKey(string base64) => Convert.FromBase64String(base64);

    /// <summary>Demo user names (for startup logs).</summary>
    public static string DescribeUsers(Development dev) => string.Join(", ", dev.Users.Keys);
}
