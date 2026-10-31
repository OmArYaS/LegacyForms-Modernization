using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace ModernForms.Api.Authorization;

/// <summary>Named role policies shared by controllers.</summary>
public static class Policies
{
    /// <summary>supervisor or admin — the queue + decide role set.</summary>
    public const string Staff = "Staff";

    /// <summary>admin only — administrative endpoints.</summary>
    public const string Admin = "Admin";
}

/// <summary>Role claims: 'employee', 'supervisor', 'admin' carried on both the dev and SSO tokens.</summary>
public static class AuthorizationSetup
{
    public static IServiceCollection AddFormsAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Staff, policy => policy.RequireAssertion(
                context => context.User.HasClaim("roles", "supervisor") || context.User.HasClaim("roles", "admin")))
            .AddPolicy(Policies.Admin, policy => policy.RequireAssertion(
                context => context.User.HasClaim("roles", "admin")));

        return services;
    }
}
