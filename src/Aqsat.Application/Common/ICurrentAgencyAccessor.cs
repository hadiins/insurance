namespace Aqsat.Application.Common;

/// <summary>
/// The tenant for the current logical operation. Task 4's auth middleware populates this from the
/// authenticated user's JWT at the start of each request; tests and the dev seeder set it directly.
/// </summary>
public interface ICurrentAgencyAccessor
{
    Guid? AgencyId { get; }
}
