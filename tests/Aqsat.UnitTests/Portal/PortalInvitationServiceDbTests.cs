using Aqsat.Domain;
using Aqsat.Infrastructure.Persistence;
using Aqsat.Infrastructure.Portal;
using Aqsat.Infrastructure.Seed;
using Aqsat.UnitTests.DataModel;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.UnitTests.Portal;

/// <summary>Reproduces the service's exact RLS flow outside HTTP: session-context stamping via
/// the interceptor, the RLS-exempt token-index lookup, and inserts under the block predicate.</summary>
public class PortalInvitationServiceDbTests
{
    [Fact]
    public async Task Insert_then_token_index_lookup_roundtrips()
    {
        await using var context = TestDbContextFactory.Create();
        var fixture = await DevSeeder.SeedAuthFixtureAsync(context);
        AgencyContext.Current = fixture.AgencyAId;

        var customer = new Customer
        {
            AgencyId = fixture.AgencyAId,
            ExternalCode = $"EXT-{Guid.NewGuid():N}"[..20],
            FullName = "مشتری تست مستقیم",
            Mobile = "09123334444",
        };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var invitation = new CustomerPortalInvitation
        {
            AgencyId = fixture.AgencyAId,
            CustomerId = customer.Id,
            Token = token,
            InquiryFeeToman = 1000,
            CreatedByUserId = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(5),
        };
        context.CustomerPortalInvitations.Add(invitation);
        context.PortalInvitationTokenIndex.Add(new PortalInvitationTokenIndex
        {
            AgencyId = fixture.AgencyAId,
            Token = token,
            InvitationId = invitation.Id,
        });
        await context.SaveChangesAsync();

        // Reset to anonymous, exactly like the public flow does.
        AgencyContext.Current = null;

        // The token index is RLS-exempt: a fresh anonymous connection must still resolve the agency.
        await using var probe = TestDbContextFactory.Create();
        var agencyId = await probe.PortalInvitationTokenIndex.AsNoTracking()
            .Where(t => t.Token == token)
            .Select(t => t.AgencyId)
            .FirstOrDefaultAsync();
        Assert.Equal(fixture.AgencyAId, agencyId);

        // Anonymous direct reads of the invitation table itself stay RLS-blocked (zero rows).
        var anonymousVisible = await probe.CustomerPortalInvitations.AsNoTracking()
            .AnyAsync(i => i.Token == token);
        Assert.False(anonymousVisible);
    }
}
