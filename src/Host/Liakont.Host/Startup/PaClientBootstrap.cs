namespace Liakont.Host.Startup;

using Liakont.PaClients.Fake;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Câblage des plug-ins de Plateforme Agréée (PA) au COMPOSITION ROOT (FIX01d) — le seul endroit
/// autorisé à référencer un plug-in PA concret (CLAUDE.md n°6/14). Les modules Transmission/Pipeline
/// ne connaissent AUCUNE PA : ils résolvent par <c>PaType</c> via le registre, qui découvre les
/// fabriques ajoutées ici. Aucune décision produit ne dépend d'un <c>if (pa is …)</c> (CLAUDE.md n°8).
/// <para>
/// Le plug-in FACTICE (PAA02 <c>Liakont.PaClients.Fake</c>) rend l'envoi exerçable de bout en bout
/// sans PA réelle : c'est la correction du bug-inbox « plug-in Fake jamais câblé au Host » (sans lui le
/// registre ne résout rien et l'envoi est inexerçable partout). Il n'est branché QU'en Development OU
/// via une section de configuration explicite (<see cref="EnableFakeConfigKey"/>) — JAMAIS par défaut
/// en production, où seules de vraies PA (clés chiffrées par tenant) sont câblées.
/// </para>
/// </summary>
public static class PaClientBootstrap
{
    /// <summary>
    /// Drapeau de configuration activant explicitement le plug-in factice hors Development (par exemple
    /// un environnement de démo hors-ligne). Absent ou <c>false</c> ⇒ inactif : la production reste sans
    /// plug-in factice tant que ce drapeau n'est pas posé délibérément.
    /// </summary>
    public const string EnableFakeConfigKey = "PaClients:Fake:Enabled";

    /// <summary>
    /// Ajoute les plug-ins PA câblés pour cet environnement. En l'état (FIX01d) : uniquement le plug-in
    /// factice, conditionné à Development OU au drapeau <see cref="EnableFakeConfigKey"/>. Les vraies PA
    /// (B2Brouter, Super PDP) s'ajouteront ici selon la même logique quand leur câblage de production
    /// (secrets chiffrés par tenant) sera défini.
    /// </summary>
    /// <param name="services">Collection de services de l'application.</param>
    /// <param name="environment">Environnement d'hébergement (Development active le plug-in factice).</param>
    /// <param name="configuration">Configuration de l'application (lecture du drapeau d'activation).</param>
    public static IServiceCollection AddConfiguredPaClients(
        this IServiceCollection services,
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);

        if (environment.IsDevelopment() || configuration.GetValue<bool>(EnableFakeConfigKey))
        {
            services.AddFakePaClient();
        }

        return services;
    }
}
