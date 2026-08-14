using Aqsat.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqsat.Infrastructure.Persistence.Configurations;

public sealed class UserOrgRoleConfiguration : AqsatEntityConfiguration<UserOrgRole>
{
    public override void Configure(EntityTypeBuilder<UserOrgRole> builder)
    {
        base.Configure(builder);

        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        builder.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrganizationId);
        builder.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId);

        builder.HasIndex(x => new { x.UserId, x.OrganizationId, x.RoleId }).IsUnique();
    }
}
