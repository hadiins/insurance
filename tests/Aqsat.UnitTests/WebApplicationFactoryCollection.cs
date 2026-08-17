namespace Aqsat.UnitTests;

/// <summary>
/// Test classes that spin up a WebApplicationFactory&lt;Program&gt; must run sequentially, not in
/// xUnit's default cross-class parallelism — starting two of them concurrently intermittently
/// throws "The entry point exited without ever building an IHost" from
/// HostFactoryResolver.HostingListener, a known flaky failure mode when multiple factories for the
/// same entry point race during startup.
/// </summary>
[CollectionDefinition("WebApplicationFactory", DisableParallelization = true)]
public class WebApplicationFactoryCollection;
