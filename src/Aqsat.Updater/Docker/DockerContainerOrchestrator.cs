using Docker.DotNet;
using Docker.DotNet.Models;

namespace Aqsat.Updater.Docker;

/// <summary>
/// docs/UPDATE-SYSTEM.md §1 — this is the only class in the whole solution that talks to
/// /var/run/docker.sock. Aqsat.Api never references this project, never mounts the socket, and
/// docker-compose.prod.yml only grants it to the "updater" service (see that file's comments) —
/// that separation is the entire security model this task exists to build.
/// </summary>
public sealed class DockerContainerOrchestrator : IContainerOrchestrator
{
    private readonly DockerClient _client;
    private readonly ILogger<DockerContainerOrchestrator> _logger;
    private readonly HttpClient _healthCheckClient;

    public DockerContainerOrchestrator(ILogger<DockerContainerOrchestrator> logger, HttpClient healthCheckClient)
    {
        _logger = logger;
        _healthCheckClient = healthCheckClient;
        // Unix socket only — this must never be reachable over TCP, even accidentally, so there is
        // no configuration knob here for a remote Docker host.
        _client = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
    }

    public async Task<string> PullImageAsync(string imageTag, CancellationToken ct)
    {
        var (repository, tag) = SplitImageTag(imageTag);

        await _client.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = repository, Tag = tag },
            authConfig: null,
            progress: new Progress<JSONMessage>(m => _logger.LogInformation("pull {Repository}:{Tag}: {Status}", repository, tag, m.Status)),
            ct);

        var inspected = await _client.Images.InspectImageAsync(imageTag, ct);
        // Return the REGISTRY manifest digest (what scripts/release/sign-package.sh signed — see
        // docs/RELEASE-RUNBOOK.md §2), not the image config ID; UpdateOrchestrator's digest gate
        // compares against exactly that signed value.
        return ImageDigest.OfPulledImage(repository, inspected.RepoDigests, inspected.ID); // sha256:... registry manifest digest of the pulled image.
    }

    public async Task<string> RecreateContainerAsync(string containerName, string imageTag, CancellationToken ct)
    {
        var existing = await _client.Containers.InspectContainerAsync(containerName, ct);
        var previousImageTag = existing.Config.Image;

        await _client.Containers.StopContainerAsync(existing.ID, new ContainerStopParameters { WaitBeforeKillSeconds = 30 }, ct);
        await _client.Containers.RemoveContainerAsync(existing.ID, new ContainerRemoveParameters(), ct);

        var created = await _client.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Name = containerName,
            Image = imageTag,
            Env = existing.Config.Env,
            ExposedPorts = existing.Config.ExposedPorts,
            HostConfig = existing.HostConfig,
            NetworkingConfig = new NetworkingConfig { EndpointsConfig = existing.NetworkSettings.Networks
                .ToDictionary(n => n.Key, n => new EndpointSettings()) },
        }, ct);

        await _client.Containers.StartContainerAsync(created.ID, new ContainerStartParameters(), ct);

        return previousImageTag;
    }

    public async Task<bool> IsHealthyAsync(string containerName, CancellationToken ct)
    {
        var inspected = await _client.Containers.InspectContainerAsync(containerName, ct);
        if (inspected.State.Running != true)
        {
            return false;
        }

        try
        {
            // Container-to-container name resolution on the compose network — same reasoning as
            // ConnectionStrings__Default using "sqlserver" as a hostname rather than an IP.
            var response = await _healthCheckClient.GetAsync($"http://{containerName}:8080/health", ct);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            return body.Contains("\"status\":\"Healthy\"", StringComparison.OrdinalIgnoreCase);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Health check request to {ContainerName} failed", containerName);
            return false;
        }
    }

    private static (string Repository, string Tag) SplitImageTag(string imageTag)
    {
        var colonIndex = imageTag.LastIndexOf(':');
        return colonIndex < 0 ? (imageTag, "latest") : (imageTag[..colonIndex], imageTag[(colonIndex + 1)..]);
    }
}
