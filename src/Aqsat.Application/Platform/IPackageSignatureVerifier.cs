namespace Aqsat.Application.Platform;

/// <summary>
/// docs/TASKS.md Task 23 — before Aqsat.Api ever writes an UpdatePackage row, it verifies the
/// signature itself, with its own copy of the same check src/Aqsat.Updater/Security/
/// PackageSignatureVerifier.cs makes later at apply time. Deliberately duplicated rather than
/// shared: Aqsat.Api and Aqsat.Updater reference nothing of each other's (docs/UPDATE-SYSTEM.md §1),
/// and a package too broken to ever be signature-checked never even makes it into the catalog a
/// Platform.Owner sees, rather than only failing much later mid-update.
/// </summary>
public interface IPackageSignatureVerifier
{
    bool Verify(string version, string imageTag, string sha256, string signatureBase64);
}
