namespace Liakont.Modules.Archive.Infrastructure;

using System;
using System.IO;
using Liakont.Modules.Archive.Application;
using Liakont.Modules.Archive.Contracts;
using Liakont.Modules.Archive.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

/// <summary>
/// Enregistrement DI du module Archive (TRK05 + TRK06) : coffre WORM, chaîne de hashes, addenda chaînés,
/// ancrage temporel, vérifieur d'intégrité et export contrôle fiscal. N'apporte AUCUNE migration : il
/// alimente <c>documents.archive_entries</c> (TRK01) et <c>documents.archive_anchors</c> (V006, créées par
/// le module Documents). Le store par défaut est <see cref="FileSystemArchiveStore"/> ; l'ancrage par
/// défaut est <see cref="NoAnchorTimestampAnchor"/> — une instance hébergée active RFC 3161 par
/// configuration (<c>Archive:Anchor</c>). Les deux choix sont du paramétrage d'INSTANCE (blueprint §6, §2 règle 6).
/// </summary>
public static class ArchiveModuleRegistration
{
    public static IServiceCollection AddArchiveModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<FileSystemArchiveStoreOptions>(configuration.GetSection("Archive:Storage:FileSystem"));
        services.PostConfigure<FileSystemArchiveStoreOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.RootPath))
            {
                // Repli sous le répertoire de l'instance ; une instance de production configure un volume dédié.
                options.RootPath = Path.Combine(AppContext.BaseDirectory, "archive-store");
            }
        });

        // Branding d'INSTANCE de la notice de réversibilité (BRD01, marque grise) : lu depuis la section
        // "Branding" (la même que la coquille et les emails du Host). Même binder fort que le Host
        // (Configure<BrandingOptions>) — voir ReadReversibilityBranding.
        services.AddSingleton(ReadReversibilityBranding(configuration));

        // Store par défaut (FileSystem). Un plug-in de store (S3, Azure, GCS) le remplace via Replace.
        services.TryAddScoped<IArchiveStore, FileSystemArchiveStore>();

        services.AddScoped<IArchiveEntryStore, PostgresArchiveEntryStore>();
        services.AddScoped<IArchiveService, ArchiveService>();

        AddAnchoring(services, configuration);

        return services;
    }

    /// <summary>
    /// Lit le branding d'instance (BRD01, marque grise) de la notice de réversibilité depuis la section
    /// "Branding" — la MÊME que la coquille et les emails du Host. Utilise le binder fort
    /// (<c>GetValue&lt;bool&gt;</c>) pour <c>PoweredByLiakont</c>, COHÉRENT avec le
    /// <c>Configure&lt;BrandingOptions&gt;</c> du Host (pas de divergence de parsing). Nom commercial vide
    /// ou absent → marque produit par défaut « Liakont ».
    /// </summary>
    internal static ReversibilityBranding ReadReversibilityBranding(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IConfigurationSection section = configuration.GetSection("Branding");
        string? commercialName = section["CommercialName"];
        return new ReversibilityBranding(
            string.IsNullOrWhiteSpace(commercialName) ? ReversibilityBranding.DefaultCommercialName : commercialName,
            section.GetValue("PoweredByLiakont", true));
    }

    /// <summary>
    /// Ancrage temporel (TRK06) : ancrage choisi par config d'instance (NoAnchor par défaut), vérifieur
    /// d'intégrité et export contrôle fiscal. Le handler de fan-out du job quotidien
    /// (<see cref="DailyAnchoringFanOutHandler"/>) est enregistré par le HOST via
    /// <c>AddJobHandler&lt;DailyAnchoringTrigger, DailyAnchoringFanOutHandler&gt;()</c> (l'extension vit dans
    /// le module Job ; l'appeler ici franchirait une frontière de module). Une méthode d'ancrage inconnue
    /// fait ÉCHOUER le démarrage (jamais un repli silencieux sur NoAnchor) — voir ADR-0011.
    /// </summary>
    private static void AddAnchoring(IServiceCollection services, IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(TimestampAnchorOptions.SectionName);
        services.Configure<TimestampAnchorOptions>(section);

        string? method = section["Method"];
        if (string.IsNullOrWhiteSpace(method) || string.Equals(method, "None", StringComparison.OrdinalIgnoreCase))
        {
            // Défaut : aucune sortie réseau. L'intégrité reste portée par la chaîne de hashes.
            services.TryAddSingleton<ITimestampAnchor, NoAnchorTimestampAnchor>();
        }
        else if (string.Equals(method, "Rfc3161", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient(HttpTsaClient.HttpClientName)
                .ConfigureHttpClient((sp, client) =>
                {
                    TimestampAnchorOptions options = sp.GetRequiredService<IOptions<TimestampAnchorOptions>>().Value;
                    client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.Rfc3161.TimeoutSeconds));
                });
            services.TryAddSingleton<ITsaClient, HttpTsaClient>();
            services.TryAddSingleton<ITimestampAnchor, Rfc3161TimestampAnchor>();
        }
        else if (string.Equals(method, "OpenTimestamps", StringComparison.OrdinalIgnoreCase))
        {
            // Présent mais non opérationnel en V1 (ADR-0011) : lève à l'usage, jamais un no-op silencieux.
            services.TryAddSingleton<ITimestampAnchor, OpenTimestampsTimestampAnchor>();
        }
        else
        {
            // Mauvaise configuration d'une fonctionnalité de sécurité : on bloque au démarrage plutôt que de
            // dégrader silencieusement vers « aucun ancrage » (une faute de frappe désactiverait le scellement).
            throw new InvalidOperationException(
                $"Méthode d'ancrage « {method} » inconnue (Archive:Anchor:Method). Valeurs acceptées : None, Rfc3161, OpenTimestamps.");
        }

        services.AddScoped<IArchiveAnchorStore, PostgresArchiveAnchorStore>();
        services.AddScoped<IArchiveAnchoringService, ArchiveAnchoringService>();
        services.AddScoped<IArchiveVerifier, ArchiveVerifier>();
        services.AddScoped<IFiscalControlExportService, FiscalControlExportService>();
        services.AddScoped<ITenantReversibilityExportService, TenantReversibilityExportService>();
    }
}
