using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Domain.Common;
using Aqsat.Domain.Enums;
using Aqsat.Infrastructure.Auth;
using Aqsat.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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
    public DbSet<PropertySubject> PropertySubjects => Set<PropertySubject>();
    public DbSet<InsuranceLine> InsuranceLines => Set<InsuranceLine>();
    public DbSet<InsuranceLineCode> InsuranceLineCodes => Set<InsuranceLineCode>();
    public DbSet<PolicyNumberFormat> PolicyNumberFormats => Set<PolicyNumberFormat>();
    public DbSet<ContractTemplate> ContractTemplates => Set<ContractTemplate>();
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<Installment> Installments => Set<Installment>();
    public DbSet<Endorsement> Endorsements => Set<Endorsement>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<Collateral> Collaterals => Set<Collateral>();
    public DbSet<CashBox> CashBoxes => Set<CashBox>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportRow> ImportRows => Set<ImportRow>();
    public DbSet<ImportColumnMapping> ImportColumnMappings => Set<ImportColumnMapping>();

    public DbSet<Marketer> Marketers => Set<Marketer>();
    public DbSet<MarketerRate> MarketerRates => Set<MarketerRate>();
    public DbSet<CommissionEntry> CommissionEntries => Set<CommissionEntry>();
    public DbSet<AgencyCommissionRate> AgencyCommissionRates => Set<AgencyCommissionRate>();
    public DbSet<AgencyCommissionEntry> AgencyCommissionEntries => Set<AgencyCommissionEntry>();
    public DbSet<RenewalWatch> RenewalWatches => Set<RenewalWatch>();

    public DbSet<ReminderLog> ReminderLogs => Set<ReminderLog>();
    public DbSet<ApiIrCallLog> ApiIrCallLogs => Set<ApiIrCallLog>();
    public DbSet<SmsTemplate> SmsTemplates => Set<SmsTemplate>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<RecordPresence> RecordPresences => Set<RecordPresence>();
    public DbSet<RecordLock> RecordLocks => Set<RecordLock>();

    // Platform-level (docs/TASKS.md Task 22) — no AgencyId, not RLS-scoped, same as
    // Organization/AppUser/Role above.
    public DbSet<UpdatePackage> UpdatePackages => Set<UpdatePackage>();
    public DbSet<UpdateRun> UpdateRuns => Set<UpdateRun>();
    public DbSet<UpdateStageLog> UpdateStageLogs => Set<UpdateStageLog>();

    private static readonly ConcurrentDictionary<Type, string[]> SensitivePropertyCache = new();

    /// <summary>
    /// CLAUDE.md rule 29: audit rows are written inside the same transaction as the change itself,
    /// via this override, so no developer can forget it. Two SaveChanges calls wrapped in one
    /// explicit transaction: the first persists the actual change and — for Added entities — lets
    /// SQL Server generate their NEWSEQUENTIALID() Id, which EF reads back into the tracked
    /// instance; only after that is EntityId/PolicyId available to build the AuditEntry for a newly
    /// created row. If anything in either phase throws, the whole transaction rolls back: neither
    /// the change nor its audit row persists.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var pendingAudits = ChangeTracker.Entries()
            .Where(e => e.Entity is IAuditableEntity && e.State is EntityState.Added or EntityState.Modified)
            .Select(e => (Entry: e, Action: e.State == EntityState.Added ? AuditAction.Created : AuditAction.Updated))
            .ToList();

        if (pendingAudits.Count == 0)
        {
            return await base.SaveChangesAsync(cancellationToken);
        }

        // Must snapshot the property-level diff before the first SaveChanges — EF clears
        // Property.IsModified / OriginalValues for an entry once it has been persisted.
        var changesByEntry = pendingAudits
            .Where(p => p.Action == AuditAction.Updated)
            .ToDictionary(p => p.Entry, p => BuildChangesJson(p.Entry));

        var actor = CurrentUserContext.Current;
        var ipAddress = CurrentRequestContext.IpAddress;

        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var affected = await base.SaveChangesAsync(cancellationToken);

            foreach (var (entry, action) in pendingAudits)
            {
                var auditable = (IAuditableEntity)entry.Entity;

                // Every IAuditableEntity implemented so far derives from AgencyOwnedEntity — this
                // is a real constraint of the design, not an incidental cast; see IAuditableEntity's
                // own doc comment on why entities without a natural AgencyId (e.g. Payment) don't
                // implement it.
                var agencyId = ((AgencyOwnedEntity)entry.Entity).AgencyId;
                var entityId = ((Entity)entry.Entity).Id;

                AuditEntries.Add(new AuditEntry
                {
                    AgencyId = agencyId,
                    UserId = actor?.UserId ?? Guid.Empty,
                    UserDisplayName = actor?.DisplayName ?? "سیستم",
                    EntityType = entry.Entity.GetType().Name,
                    EntityId = entityId,
                    PolicyId = auditable.PolicyId,
                    Action = action,
                    Description = auditable.DescribeChange(action),
                    ChangesJson = action == AuditAction.Updated ? changesByEntry[entry] : null,
                    OccurredAt = DateTimeOffset.UtcNow,
                    IpAddress = ipAddress,
                });
            }

            affected += await base.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return affected;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Never includes a property marked [AuditSensitive] (rule 30) — those record only that they
    /// changed, never the before/after value. Housekeeping columns (Id/BizId/RowVersion) are
    /// excluded as noise; they change on every write and carry no business meaning.
    /// </summary>
    private static string? BuildChangesJson(EntityEntry entry)
    {
        var sensitive = GetSensitivePropertyNames(entry.Entity.GetType());
        var changed = new SortedDictionary<string, object?>();

        foreach (var property in entry.Properties)
        {
            if (!property.IsModified)
            {
                continue;
            }

            var name = property.Metadata.Name;
            if (name is nameof(Entity.Id) or nameof(Entity.BizId) or nameof(SoftDeletableEntity.RowVersion))
            {
                continue;
            }

            changed[name] = sensitive.Contains(name) ? "changed" : property.CurrentValue;
        }

        return changed.Count == 0 ? null : JsonSerializer.Serialize(changed);
    }

    private static string[] GetSensitivePropertyNames(Type entityType) =>
        SensitivePropertyCache.GetOrAdd(entityType, t => t.GetProperties()
            .Where(p => p.GetCustomAttribute<AuditSensitiveAttribute>() is not null)
            .Select(p => p.Name)
            .ToArray());

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // CustomerConfiguration and MarketerConfiguration each need an IFieldEncryptor instance to
        // build their NationalId value converter, so neither can be discovered via the assembly
        // scan (which requires a parameterless constructor) — applied explicitly instead.
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AppDbContext).Assembly,
            t => t != typeof(CustomerConfiguration) && t != typeof(MarketerConfiguration));
        modelBuilder.ApplyConfiguration(new CustomerConfiguration(fieldEncryptor));
        modelBuilder.ApplyConfiguration(new MarketerConfiguration(fieldEncryptor));

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
