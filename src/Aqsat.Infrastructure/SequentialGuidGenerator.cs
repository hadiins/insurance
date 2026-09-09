namespace Aqsat.Infrastructure;

/// <summary>
/// The C# counterpart of NEWSEQUENTIALID() (CLAUDE.md rule 1). Assigned when an entity's Id must be
/// known before SaveChanges — a manual audit row written in the same SaveChanges needs the final
/// EntityId, and a database-generated key is only readable after the insert.
/// </summary>
public static class SequentialGuidGenerator
{
    private static readonly object Sync = new();
    private static long _lastStamp;

    public static Guid Next()
    {
        long stamp;
        lock (Sync)
        {
            var now = DateTime.UtcNow.Ticks;
            stamp = now > _lastStamp ? now : _lastStamp + 1;
            _lastStamp = stamp;
        }
        return Build(stamp);
    }

    private static Guid Build(long stamp)
    {
        // SQL Server compares uniqueidentifiers by bytes 10-15 first, in ascending order — the
        // timestamp goes there big-endian so keys ascend with insert order; the rest stays random
        // so two keys minted in the same tick still differ.
        var bytes = Guid.NewGuid().ToByteArray();
        bytes[15] = (byte)(stamp & 0xFF);
        bytes[14] = (byte)((stamp >> 8) & 0xFF);
        bytes[13] = (byte)((stamp >> 16) & 0xFF);
        bytes[12] = (byte)((stamp >> 24) & 0xFF);
        bytes[11] = (byte)((stamp >> 32) & 0xFF);
        bytes[10] = (byte)((stamp >> 40) & 0xFF);
        return new Guid(bytes);
    }
}
