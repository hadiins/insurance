namespace Aqsat.UnitTests.Updater;

/// <summary>
/// Task 21's other check (docs/TASKS.md): "remove the Docker socket from the main API — everything
/// still works." The way to guarantee that is to never grant it in the first place. This is a
/// regression guard on docker-compose.prod.yml itself so a future edit can't silently add socket
/// access to "api" — no live Docker required, Docker isn't even available in this dev environment
/// (see docs/RESTORE-RUNBOOK.md for how that constraint was handled for Task 20).
/// </summary>
public class DockerSocketIsolationTests
{
    [Fact]
    public void The_api_service_block_never_mounts_the_docker_socket_and_only_updater_does()
    {
        var composePath = FindComposeFile();
        var lines = File.ReadAllLines(composePath);

        var serviceBlocks = SplitTopLevelServiceBlocks(lines);

        Assert.True(serviceBlocks.ContainsKey("api"), "docker-compose.prod.yml should define an 'api' service.");
        Assert.True(serviceBlocks.ContainsKey("updater"), "docker-compose.prod.yml should define an 'updater' service.");

        Assert.DoesNotContain("docker.sock", serviceBlocks["api"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("docker.sock", serviceBlocks["updater"], StringComparison.OrdinalIgnoreCase);

        // The updater service must not publish a host port either (docs/UPDATE-SYSTEM.md §1:
        // "بدون پورت عمومی") — a top-level "ports:" key inside its own block would be the mistake.
        Assert.DoesNotContain("\n    ports:", serviceBlocks["updater"]);
    }

    [Fact]
    public void The_api_is_not_published_on_every_interface()
    {
        var lines = File.ReadAllLines(FindComposeFile());
        var api = SplitTopLevelServiceBlocks(lines)["api"];

        // "8080:8080" publishes plain HTTP on 0.0.0.0, which is reachable directly and bypasses the
        // TLS-terminating reverse proxy in front of it. Binding loopback keeps the proxy working
        // while making a direct network connection impossible.
        Assert.DoesNotContain("\n      - \"8080:8080\"", api);
        Assert.Contains("127.0.0.1:8080:8080", api);
    }

    [Fact]
    public void The_api_has_a_persistent_log_volume()
    {
        var lines = File.ReadAllLines(FindComposeFile());
        var api = SplitTopLevelServiceBlocks(lines)["api"];

        // Serilog writes logs/aqsat-api-.log. Inside the container's writable layer it is destroyed
        // by every recreate — and the updater recreates the container on EVERY release, so the log
        // would vanish exactly when an update misbehaves. A named volume preserves it.
        Assert.Contains("api-logs:/app/logs", api);
    }

    [Fact]
    public void The_api_trusts_its_reverse_proxy_network_for_forwarded_headers()
    {
        var lines = File.ReadAllLines(FindComposeFile());
        var api = SplitTopLevelServiceBlocks(lines)["api"];

        // Without a known proxy, every request through the proxy shares one RemoteIpAddress: the
        // per-IP login limiter becomes a single bucket for the whole office and every audit row
        // records the proxy instead of the actor.
        Assert.Contains("Deployment__KnownNetworks__0", api);
    }

    [Fact]
    public void There_is_no_code_path_anywhere_that_can_drop_the_database()
    {
        // Deployment:ResetDatabaseOnStartup used to DROP the whole database at startup, gated behind
        // env vars. Aqsat.Updater recreates the api container COPYING THE OLD CONTAINER'S ENV, so one
        // flag left behind in a .env would wipe every agency's data on the next update or restart —
        // and again on every app-pool recycle after that. The feature is now GONE, not merely
        // double-gated: there is no configuration value that can re-enable it.
        var root = FindRepositoryRoot();
        var offenders = new List<string>();

        // Scoped to the solution's own source trees. The repository root also contains other
        // agents' worktrees (.kilo/, .claude/) whose checkouts are not ours to judge.
        foreach (var projectRoot in new[] { "src", "tests" })
        {
            var directory = Path.Combine(root, projectRoot);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                // Tests legitimately NAME the removed flags in the guard that keeps them out.
                if (projectRoot == "tests")
                {
                    continue;
                }

                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var text = File.ReadAllText(file);
                if (text.Contains("ResetDatabaseOnStartup", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("AllowProductionDatabaseReset", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("DROP DATABASE", StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add(Path.GetRelativePath(root, file));
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"Database-dropping code reintroduced in: {string.Join(", ", offenders)}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "docker-compose.prod.yml")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new FileNotFoundException("Could not locate the repository root by walking up from the test output directory.");
        }

        return directory.FullName;
    }

    private static string FindComposeFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docker-compose.prod.yml")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new FileNotFoundException("Could not locate docker-compose.prod.yml by walking up from the test output directory.");
        }

        return Path.Combine(directory.FullName, "docker-compose.prod.yml");
    }

    /// <summary>Crude but sufficient for this file's actual shape: top-level service names are the
    /// only lines indented exactly two spaces and ending in ':' under the "services:" section.</summary>
    private static Dictionary<string, string> SplitTopLevelServiceBlocks(string[] lines)
    {
        var blocks = new Dictionary<string, string>();
        string? currentService = null;
        var currentLines = new List<string>();
        var inServicesSection = false;

        foreach (var line in lines)
        {
            if (line == "services:")
            {
                inServicesSection = true;
                continue;
            }

            if (!inServicesSection)
            {
                continue;
            }

            if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
            {
                // Dedent back to column 0 (e.g. the top-level "volumes:" key) ends the services section.
                break;
            }

            var isTopLevelServiceName = line.StartsWith("  ") && !line.StartsWith("   ") && line.TrimEnd().EndsWith(':');
            if (isTopLevelServiceName)
            {
                if (currentService is not null)
                {
                    blocks[currentService] = string.Join('\n', currentLines);
                }

                currentService = line.Trim().TrimEnd(':');
                currentLines = [];
                continue;
            }

            currentLines.Add(line);
        }

        if (currentService is not null)
        {
            blocks[currentService] = string.Join('\n', currentLines);
        }

        return blocks;
    }
}
