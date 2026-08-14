using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Domain.Common;
using Aqsat.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, IFieldEncryptor fieldEncryptor)
    : DbContext(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrgSettings> OrgSettings => Set<OrgSettings>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserOrgRole> UserOrgRoles => Set<UserOrgRole>();

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<ContractTemplate> ContractTemplates => Set<ContractTemplate>();
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<Installment> Installments => Set<Installment>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<Collateral> Collaterals => Set<Collateral>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportRow> ImportRows => Set<ImportRow>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<RecordPresence> RecordPresences => Set<RecordPresence>();
    public DbSet<RecordLock> RecordLocks => Set<RecordLock>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // CustomerConfiguration needs an IFieldEncryptor instance to build the NationalId value
        // converter, so it can't be discovered via the assembly scan (which requires a
        // parameterless constructor) — applied explicitly instead.
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AppDbContext).Assembly,
            t => t != typeof(CustomerConfiguration));
        modelBuilder.ApplyConfiguration(new CustomerConfiguration(fieldEncryptor));

        // No hard deletes anywhere (CLAUDE.md rule 7) — soft-deleted rows never come back from a
        // normal query. This is the ONLY global EF filter; AgencyId isolation is 100% DB-side RLS.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(SoftDeletableEntity).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");
                var property = System.Linq.Expressions.Expression.Property(parameter, nameof(SoftDeletableEntity.IsDeleted));
                var notDeleted = System.Linq.Expressions.Expression.Not(property);
                var lambda = System.Linq.Expressions.Expression.Lambda(notDeleted, parameter);
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
            }
        }

        // EF Core defaults to Cascade — every generated migration must be read line by line to
        // confirm this loop actually ran (CLAUDE.md rule 6, verbatim).
        foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
        {
            foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }
}
