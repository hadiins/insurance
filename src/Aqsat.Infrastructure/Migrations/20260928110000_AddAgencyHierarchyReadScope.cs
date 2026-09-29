using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aqsat.Infrastructure.Migrations
{
    /// <summary>
    /// CLAUDE.md rule 10, extended to the Organization hierarchy: Headquarters and Regional tenants
    /// must be able to READ the rows of every tenant below them, while one agency's data stays
    /// invisible to its siblings.
    ///
    /// Two deliberate choices, both because of how big the tree already is:
    ///
    /// 1. Only the FILTER predicates move. The BLOCK predicates keep the original strict-equality
    ///    fn_AgencyAccessPredicate, so a widened read can NEVER write into a subordinate agency's
    ///    books — an aggregate view that silently posts a payment into a child agency's ledger would
    ///    be worse than the missing feature. A write still belongs to the organization that owns it.
    ///
    /// 2. Widening is opt-in per connection, through SESSION_CONTEXT(N'AgencyHierarchyRead').
    ///    SignupController already parents EVERY self-served agency under the single Headquarters
    ///    org (ParentId = hq.Id), and the owner's session resolves to that same HQ org (see
    ///    AgencyStatsDaily's doc comment). So an ambient widening would, on the first request after
    ///    this migration, expand every RLS table from one tenant to the whole tenant tree for every
    ///    owner request — a semantic change (single-row assumptions, per-agency totals) and a
    ///    performance cliff, both on the day of deploy. With the flag absent, the new predicate is
    ///    as strict as the old one, so deploying this changes nothing until a request asks for
    ///    hierarchy scope explicitly.
    ///
    /// The table list is read from sys.sql_security_predicates rather than restated here: the policy
    /// has been extended by a dozen later migrations, and a hand-copied list is how a table gets
    /// left behind on the strict predicate while the rest moved.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260928110000_AddAgencyHierarchyReadScope")]
    public partial class AddAgencyHierarchyReadScope : Migration
    {
        /// <summary>Re-points every FILTER predicate of the policy currently bound to
        /// <paramref name="fromFunction"/> at <paramref name="toFunction"/>, one statement per
        /// table. STRING_AGG is avoided on purpose (SQL Server 2016 has none, and this project ships
        /// to shared hosting where the engine version is not ours to choose). The two names are
        /// inlined because MigrationBuilder.Sql takes no parameters in this EF version — both call
        /// sites pass literals from this file, so nothing user-controlled reaches the text.
        ///
        /// Column reference verified against the live catalog (SQL Server 2025): the view is
        /// sys.security_predicates joined to sys.security_policies ON object_id (there is no
        /// security_policy_id column), FILTER rows are predicate_type_desc = 'FILTER' (their
        /// operation/operation_desc are NULL, which is why the ALTER carries no AFTER clause), and
        /// the predicate's ARGUMENT column is not a catalog column at all — it only exists inside
        /// predicate_definition, whose shape is
        /// <c>([dbo].[fn_AgencyAccessPredicate]([AgencyId]))</c>, so it is parsed back out rather
        /// than assumed to be AgencyId: a table filtered on some other column must be rehomed with
        /// its own argument, not silently rewritten to AgencyId.</summary>
        private static string RehomeFilterPredicates(string toFunction, string fromFunction) =>
            $"""
            DECLARE @sql nvarchar(max) = NULL;
            DECLARE @toFunction sysname = N'{toFunction}';
            DECLARE @fromPattern nvarchar(400) = N'%{fromFunction}%';

            SELECT @sql = COALESCE(@sql + N';' + CHAR(10), N'')
                + N'ALTER SECURITY POLICY dbo.AgencyAccessPolicy'
                + N' ALTER FILTER PREDICATE dbo.' + @toFunction + N'(' + arg.col + N') ON '
                + QUOTENAME(OBJECT_SCHEMA_NAME(p.target_object_id))
                + N'.' + QUOTENAME(OBJECT_NAME(p.target_object_id))
            FROM sys.security_predicates AS p
            JOIN sys.security_policies AS sp ON sp.object_id = p.object_id
            CROSS APPLY (
                -- def = ([dbo].[fn_X]([AgencyId])) -> the bracketed argument between '](' and the
                -- next ']' , stripped of brackets so QUOTENAME can re-apply them safely.
                SELECT col = QUOTENAME(
                    REPLACE(REPLACE(
                        SUBSTRING(p.predicate_definition,
                            close_at.open_paren + 3,
                            CHARINDEX(N']', p.predicate_definition, close_at.open_paren + 3)
                                - close_at.open_paren - 3),
                        N'[', N''), N']', N''))
                FROM (SELECT open_paren = CHARINDEX(N'](', p.predicate_definition)) AS close_at
            ) AS arg
            WHERE sp.name = N'AgencyAccessPolicy'
              AND p.predicate_type_desc = N'FILTER'   -- BLOCK predicates stay strict on purpose
              AND p.predicate_definition LIKE @fromPattern;

            IF @sql IS NOT NULL
                EXEC sp_executesql @sql;
            """;

        /// <summary>
        /// The acting organization plus its descendants, expanded from Organizations.ParentId.
        ///
        /// SCHEMABINDING is mandatory (RLS requires it) and it forbids a recursive CTE, so the
        /// expansion is written out to three levels below the root, which covers the entire
        /// OrganizationLevel enum (Agency under Regional under Headquarters) with a level to spare.
        /// Nesting deeper than that yields a NARROWER scope, never a wider one; extending this list
        /// is the fix if a fourth level is ever introduced.
        ///
        /// IsActive/IsDeleted are deliberately NOT consulted: whether a suspended tenant's history
        /// stays readable by its parent is a product question, not a tenant-isolation one, and EF's
        /// soft-delete query filter still hides deleted rows from the application either way. Only
        /// Id and ParentId are referenced, which keeps the schema binding as small as possible.
        /// </summary>
        private const string CreateSubtreeFunction =
            """
            CREATE OR ALTER FUNCTION dbo.fn_AgencySubtree(@RootId uniqueidentifier)
            RETURNS TABLE
            WITH SCHEMABINDING
            AS
            RETURN
                SELECT @RootId AS AgencyId
            UNION ALL
                SELECT lvl1.Id
                FROM dbo.Organizations AS lvl1
                WHERE lvl1.ParentId = @RootId
            UNION ALL
                SELECT lvl2.Id
                FROM dbo.Organizations AS lvl1
                JOIN dbo.Organizations AS lvl2 ON lvl2.ParentId = lvl1.Id
                WHERE lvl1.ParentId = @RootId
            UNION ALL
                SELECT lvl3.Id
                FROM dbo.Organizations AS lvl1
                JOIN dbo.Organizations AS lvl2 ON lvl2.ParentId = lvl1.Id
                JOIN dbo.Organizations AS lvl3 ON lvl3.ParentId = lvl2.Id
                WHERE lvl1.ParentId = @RootId;
            """;

        /// <summary>
        /// Plain strict equality whenever SESSION_CONTEXT(N'AgencyHierarchyRead') is absent or not
        /// '1'. A missing AgencyId scope stays fail-closed: CAST(NULL) makes the subtree's root row
        /// NULL, and <c>@AgencyId IN (NULL)</c> evaluates to UNKNOWN, so an unstamped connection
        /// reads nothing at all — the same shape as the old predicate's <c>= NULL</c>.
        /// </summary>
        private const string CreateReadPredicateFunction =
            """
            CREATE OR ALTER FUNCTION dbo.fn_AgencyReadPredicate(@AgencyId uniqueidentifier)
            RETURNS TABLE
            WITH SCHEMABINDING
            AS
            RETURN SELECT 1 AS fn_result
            WHERE @AgencyId = CAST(SESSION_CONTEXT(N'AgencyId') AS uniqueidentifier)
               OR (
                    CAST(SESSION_CONTEXT(N'AgencyHierarchyRead') AS nvarchar(1)) = N'1'
                    AND @AgencyId IN (
                        SELECT s.AgencyId
                        FROM dbo.fn_AgencySubtree(
                            CAST(SESSION_CONTEXT(N'AgencyId') AS uniqueidentifier)) AS s)
                  );
            """;

        protected override void Up(MigrationBuilder db)
        {
            // Declared before the functions because they are schema-bound to Organizations.ParentId;
            // the index is what keeps the subtree expansion a seek instead of a scan of every tenant.
            db.Sql("""
                IF NOT EXISTS (SELECT 1 FROM sys.indexes
                               WHERE object_id = OBJECT_ID(N'[dbo].[Organizations]')
                                 AND name = N'IX_Organizations_ParentId')
                    CREATE INDEX [IX_Organizations_ParentId]
                        ON [dbo].[Organizations] ([ParentId]) INCLUDE ([Id]);
                """);

            db.Sql(CreateSubtreeFunction);
            db.Sql(CreateReadPredicateFunction);

            db.Sql(RehomeFilterPredicates("fn_AgencyReadPredicate", "fn_AgencyAccessPredicate"));
        }

        protected override void Down(MigrationBuilder db)
        {
            // Order matters: the functions cannot be dropped while the policy still references them.
            db.Sql(RehomeFilterPredicates("fn_AgencyAccessPredicate", "fn_AgencyReadPredicate"));
            db.Sql("DROP FUNCTION IF EXISTS dbo.fn_AgencyReadPredicate;");
            db.Sql("DROP FUNCTION IF EXISTS dbo.fn_AgencySubtree;");
            db.Sql("""
                IF EXISTS (SELECT 1 FROM sys.indexes
                           WHERE object_id = OBJECT_ID(N'[dbo].[Organizations]')
                             AND name = N'IX_Organizations_ParentId')
                    DROP INDEX [IX_Organizations_ParentId] ON [dbo].[Organizations];
                """);
        }
    }
}

