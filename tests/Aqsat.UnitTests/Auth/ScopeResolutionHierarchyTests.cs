using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Aqsat.Domain;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Auth;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Auth;

/// <summary>
/// The gate in front of the widened RLS filter (migration AddAgencyHierarchyReadScope). Drives
/// ScopeResolutionMiddleware directly — it is plain middleware taking its DbContext, so the whole
/// WebApplicationFactory is not needed to prove which requests get a subtree read.
///
/// The asymmetry is the point: reads widen only when the caller ASKS (X-Organization-Scope: tree)
/// AND the organization it is acting as actually sits above other tenants. Every self-served agency
/// is parented under the single Headquarters org, so an ambient widening would quietly turn the
/// owner's own dashboard into a cross-tenant scan.
/// </summary>
public class ScopeResolutionHierarchyTests
{
    [Theory]
    [InlineData(OrganizationLevel.Headquarters, "tree", true)]
    [InlineData(OrganizationLevel.Headquarters, "TREE", true)]  // header value is case-insensitive
    [InlineData(OrganizationLevel.Regional, "tree", true)]
    [InlineData(OrganizationLevel.Agency, "tree", false)]       // a leaf must not widen itself
    [InlineData(OrganizationLevel.Headquarters, null, false)]   // no header, no widening
    [InlineData(OrganizationLevel.Headquarters, "own", false)]  // any other value is strict
    public async Task The_tree_header_widens_reads_only_for_a_tenant_above_other_tenants(
        OrganizationLevel level, string? scopeHeader, bool expectWidened)
    {
        await using var context = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(context);
        var organizationId = level switch
        {
            OrganizationLevel.Headquarters => fixture.HeadquartersId,
            OrganizationLevel.Regional => fixture.RegionalId,
            _ => fixture.AgencyAId,
        };

        var userId = await SeedMembershipAsync(context, organizationId);

        var (hierarchyRead, actedOrganizationId) = await RunAsync(context, userId, organizationId, scopeHeader);

        Assert.Equal(organizationId, actedOrganizationId);
        Assert.Equal(expectWidened, hierarchyRead);
    }

    /// <summary>Runs the middleware over a request authenticated as <paramref name="userId"/> and
    /// reports the scope it left behind. Returns the ambient values rather than a status code
    /// because the widening IS the ambient value the rest of the request reads.</summary>
    private static async Task<(bool HierarchyRead, Guid OrganizationId)> RunAsync(
        AppDbContext context, Guid userId, Guid organizationId, string? scopeHeader)
    {
        var httpContext = new DefaultHttpContext();
        // authenticationType must be non-null or the identity is not authenticated and the
        // middleware short-circuits before it ever resolves a scope.
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()) }, "Test"));
        httpContext.Request.Headers["X-Organization-Id"] = organizationId.ToString();
        if (scopeHeader is not null)
        {
            httpContext.Request.Headers[ScopeResolutionMiddleware.HierarchyScopeHeader] = scopeHeader;
        }

        // Poison the ambient state first: if the middleware only ever assigned `true`, a stale
        // widened scope from an earlier request in this flow would make a failing case pass.
        AgencyContext.Current = Guid.NewGuid();
        AgencyContext.HierarchyRead = true;

        var reachedPipeline = false;
        bool? observedHierarchyRead = null;
        Guid? observedOrganizationId = null;
        // Read the scope from INSIDE the terminal delegate: AsyncLocal writes only flow DOWNSTREAM
        // (the ASP.NET pipeline continues inside InvokeAsync), so reading AgencyContext after the
        // await would just show this method's own poisoned values. Inside the delegate is precisely
        // where a real controller reads it.
        var middleware = new ScopeResolutionMiddleware(_ =>
        {
            reachedPipeline = true;
            observedHierarchyRead = AgencyContext.HierarchyRead;
            observedOrganizationId = AgencyContext.Current;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(httpContext, context);

        Assert.True(reachedPipeline, "request should not have been rejected");
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        return (observedHierarchyRead!.Value, observedOrganizationId!.Value);
    }

    /// <summary>A Manager-role user with a single membership in the given organization — the shape
    /// the middleware resolves to exactly one org regardless of the X-Organization-Id header.</summary>
    private static async Task<Guid> SeedMembershipAsync(AppDbContext context, Guid organizationId)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var role = new Role { Name = $"Scope-{suffix}" };
        var user = new AppUser
        {
            FullName = "کاربر آزمایشی",
            Mobile = $"09{Random.Shared.Next(10_000_000, 99_999_999)}",
            PasswordHash = "not-used-by-this-middleware",
            IsActive = true,
        };
        context.Roles.Add(role);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        // None of these tables carry AgencyId (identity tables are the RLS exemption), so no scope
        // is needed here — same reason the real login flow works before any scope exists.
        context.UserOrgRoles.Add(new UserOrgRole
        {
            UserId = user.Id,
            OrganizationId = organizationId,
            RoleId = role.Id,
        });
        await context.SaveChangesAsync();

        return user.Id;
    }
}
