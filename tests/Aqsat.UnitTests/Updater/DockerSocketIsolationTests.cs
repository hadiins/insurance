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
