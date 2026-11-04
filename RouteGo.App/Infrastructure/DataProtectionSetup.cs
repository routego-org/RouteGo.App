using Microsoft.AspNetCore.DataProtection;

namespace RouteGo.App.Infrastructure;

// Data Protection key ring (skills-aspnetcore-project-setup/references/data-protection.md).
// Antiforgery tokens, the TempData flash cookie, and the auth cookie are encrypted with this ring.
// A per-process ring dies with the container, so every restart would log everyone out and break
// every open form. The Portal runs as one container on routego-edge, so the ring lives on the
// docker volume mounted at /keys (Web Portal FRD 3.2.4) and survives a restart or a redeploy.
//
// The folder is a constant, never read from configuration: the volume mount names the same path.
// Guarded on Development only: local runs and tests keep the ephemeral per-process ring (one
// process, nothing to persist, no /keys folder on a laptop). More than one Portal instance would
// need a shared store (S3, SSM, a database) instead — a deliberate discussion point in the FRD.
public static class DataProtectionSetup
{
    /// <summary>Folder the key ring is persisted under — the docker volume mount point.</summary>
    public const string KeyRingDirectory = "/keys";

    // Pinned so the keys stay valid when the content root changes between image builds; the
    // default application discriminator is derived from that path.
    private const string ApplicationName = "routego-app";

    /// <summary>Persists the key ring to the /keys volume everywhere except Development.</summary>
    /// <param name="services">The application's service collection.</param>
    /// <param name="environment">The hosting environment; Development keeps the ephemeral ring.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddProjectDataProtection(
        this IServiceCollection services, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            return services;
        }

        services.AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(KeyRingDirectory));
        return services;
    }
}
