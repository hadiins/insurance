namespace Aqsat.Updater.Docker;

/// <summary>
/// The only boundary in this service that touches the Docker socket. Everything above this
/// interface (UpdateOrchestrator, the minimal-API endpoints) is plain C# and unit-testable without
/// Docker; only <see cref="DockerContainerOrchestrator"/> needs a real daemon.
/// </summary>
public interface IContainerOrchestrator
{
    /// <summary>Pulls the image and returns its content digest (sha256:...), so the caller can
    /// verify it against the manifest before touching any running container.</summary>
    Task<string> PullImageAsync(string imageTag, CancellationToken ct);

    /// <summary>Stops and replaces the named container with a new one from <paramref name="imageTag"/>,
    /// reusing the existing container's env vars, volumes, network, ports and restart policy. Returns
    /// the previous image tag, so the caller can roll back to it without having to remember it separately.</summary>
    Task<string> RecreateContainerAsync(string containerName, string imageTag, CancellationToken ct);

    /// <summary>True once the container is running AND its health endpoint responds successfully.
    /// Callers should retry this over a bounded window, not treat one false as final.</summary>
    Task<bool> IsHealthyAsync(string containerName, CancellationToken ct);
}
