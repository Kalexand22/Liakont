namespace Liakont.Host.Startup;

using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Liakont.Host.AgentApi;
using Liakont.Host.Behaviors;
using Liakont.Host.Clients;
using Liakont.Host.Components;
using Liakont.Host.Configuration;
using Liakont.Host.FleetApi;
using Liakont.Host.Localization;
using Liakont.Host.MultiTenancy;
using Liakont.Host.Navigation;
using Liakont.Host.Notifications;
using Liakont.Host.PaDelivery;
using Liakont.Host.Security;
using Liakont.Host.Security.Abstractions;
using Liakont.Host.Security.Keycloak;
using Liakont.Host.Services;
using Liakont.Host.Signature;
using Liakont.Host.Staging;
using Liakont.Modules.Archive.Infrastructure;
using Liakont.Modules.Archive.Web;
using Liakont.Modules.DocumentApproval.Infrastructure;
using Liakont.Modules.Documents.Infrastructure;
using Liakont.Modules.Documents.Web;
using Liakont.Modules.FacturX.Infrastructure;
using Liakont.Modules.FleetSupervision.Application;
using Liakont.Modules.FleetSupervision.Infrastructure;
using Liakont.Modules.Ingestion.Application;
using Liakont.Modules.Ingestion.Infrastructure;
using Liakont.Modules.Ingestion.Web;
using Liakont.Modules.Mandats.Infrastructure;
using Liakont.Modules.Mandats.Infrastructure.TacitAcceptance;
using Liakont.Modules.Payments.Infrastructure;
using Liakont.Modules.Pipeline.Contracts.Jobs;
using Liakont.Modules.Pipeline.Infrastructure;
using Liakont.Modules.Pipeline.Infrastructure.Send;
using Liakont.Modules.Pipeline.Web;
using Liakont.Modules.Reconciliation.Infrastructure;
using Liakont.Modules.Reconciliation.Web;
using Liakont.Modules.Signature.Application;
using Liakont.Modules.Signature.Contracts;
using Liakont.Modules.Signature.Infrastructure;
using Liakont.Modules.Signature.Infrastructure.Drain;
using Liakont.Modules.Signature.Web;
using Liakont.Modules.Staging.Contracts;
using Liakont.Modules.Staging.Infrastructure;
using Liakont.Modules.Supervision.Application;
using Liakont.Modules.Supervision.Infrastructure;
using Liakont.Modules.SupportTrace.Infrastructure;
using Liakont.Modules.TenantSettings.Infrastructure;
using Liakont.Modules.TenantSettings.Web;
using Liakont.Modules.Transmission.Contracts;
using Liakont.Modules.Transmission.Infrastructure;
using Liakont.Modules.TvaMapping.Infrastructure;
using Liakont.Modules.TvaMapping.Web;
using Liakont.Modules.Validation.Infrastructure;
using Liakont.PaClients.SuperPdp;
using Liakont.SignatureProviders.Yousign;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Stratum.Common.Abstractions.Jobs;
using Stratum.Common.Abstractions.MultiTenancy;
using Stratum.Common.Abstractions.Security;
using Stratum.Common.Infrastructure.Actions;
using Stratum.Common.Infrastructure.Audit;
using Stratum.Common.Infrastructure.Collaboration;
using Stratum.Common.Infrastructure.CrossTenant;
using Stratum.Common.Infrastructure.Database;
using Stratum.Common.Infrastructure.DataIsolation;
using Stratum.Common.Infrastructure.Events;
using Stratum.Common.Infrastructure.Gis;
using Stratum.Common.Infrastructure.GridPreferences;
using Stratum.Common.Infrastructure.HealthChecks;
using Stratum.Common.Infrastructure.Http;
using Stratum.Common.Infrastructure.Jobs;
using Stratum.Common.Infrastructure.Keycloak;
using Stratum.Common.Infrastructure.UiRules;
using Stratum.Common.Infrastructure.Validation;
using Stratum.Common.UI;
using Stratum.Common.UI.Models;
using Stratum.Modules.Audit.Infrastructure;
using Stratum.Modules.Audit.Web;
using Stratum.Modules.Identity.Application;
using Stratum.Modules.Identity.Contracts.Queries;
using Stratum.Modules.Identity.Infrastructure;
using Stratum.Modules.Identity.Web;
using Stratum.Modules.Job.Infrastructure;
using Stratum.Modules.Job.Web;
using Stratum.Modules.Notification.Contracts;
using Stratum.Modules.Notification.Contracts.DTOs;
using Stratum.Modules.Notification.Infrastructure;
using Stratum.Modules.Notification.Infrastructure.Handlers.Jobs;
using Stratum.Modules.Notification.Web;

/// <summary>
/// Centralises service registration, middleware configuration, and endpoint mapping
/// so that both the production entry-point (<c>Program.cs</c>) and E2E tests can
/// share the same application setup over different hosting strategies
/// (Kestrel in production, Kestrel-on-dynamic-port in tests).
/// </summary>
public static class AppBootstrap
{
    /// <summary>Registers all application services on the given builder.</summary>
    public static void ConfigureServices(WebApplicationBuilder builder)
    {
        // Localization
        builder.Services.AddLocalization();

        // Cache court de la préférence de langue persistée (PersistedLanguageRequestCultureProvider) :
        // la base reste la source de vérité, sans lecture par requête.
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<Liakont.Host.Localization.UserCultureCache>();

        // Localized tab title provider — registered before AddCommonUI so TryAdd doesn't override.
        builder.Services.AddScoped<Stratum.Common.UI.Services.ITabTitleProvider, LocalizedTabTitleProvider>();

        // Common UI services (IToastService, IConnectionStatusService)
        builder.Services.AddCommonUI(builder.Configuration);

        // Persistance du trousseau Data Protection (CFG02) — exigence appliance (F12 §6.2). En
        // conteneur, sans persistance explicite le trousseau vit dans le système de fichiers ÉPHÉMÈRE
        // du conteneur : il est RÉGÉNÉRÉ à chaque redéploiement et les secrets tenant chiffrés (clés
        // API des PA) deviennent ILLISIBLES. Quand DataProtection:KeyRingPath est renseigné (volume
        // monté), le trousseau y survit. Non renseigné (dev/test) : comportement inchangé — le module
        // TenantSettings pose déjà ApplicationName "Liakont" sur le trousseau par défaut.
        var dataProtectionKeyRingPath = builder.Configuration["DataProtection:KeyRingPath"];
        if (!string.IsNullOrWhiteSpace(dataProtectionKeyRingPath))
        {
            Directory.CreateDirectory(dataProtectionKeyRingPath);
            builder.Services.AddDataProtection()
                .SetApplicationName("Liakont")
                .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyRingPath));
        }

        // Infrastructure
        builder.Services.AddStratumDatabase(builder.Configuration);
        builder.Services.AddStratumEvents();
        builder.Services.AddStratumAudit();
        builder.Services.AddStratumHealthChecks();
        builder.Services.AddGridPreferences();
        builder.Services.AddStratumCompanyFilter();
        builder.Services.AddStratumMultiTenancy(builder.Configuration);
        builder.Services.AddStratumCollaboration();
        builder.Services.AddCrossTenantPublisher();
        builder.Services.AddCrossTenantDispatcher(builder.Configuration);
        builder.Services.AddCrossTenantHandlers(typeof(Stratum.Common.Infrastructure.CrossTenant.TestPing.InboundPingHandler).Assembly);
        builder.Services.AddStratumActionPipeline();
        builder.Services.AddStratumValidationEngine();
        builder.Services.AddStratumUiRuleEngine();
        builder.Services.AddStratumGis(builder.Configuration);

        // Multi-tenant job runner (SOL06) — fans an ITenantJob out over all active tenants.
        // Requires ITenantScopeFactory (registered by AddStratumMultiTenancy above). The optional per-tenant
        // time budget (RDL08, A6-scale-3) is bound from the "TenantJobs" section; absent → disabled (null).
        builder.Services.AddTenantJobs(opts =>
            builder.Configuration.GetSection(TenantJobRunnerOptions.SectionName).Bind(opts));

        // De-duplication guard for the recurring scheduler (RDL08, A6-scale-2): consulted by JobScheduler
        // before enqueuing, suppresses an enqueue when a Pending job of the same type/scope already exists.
        builder.Services.AddScoped<IRecurringJobEnqueueGuard, RecurringJobEnqueueGuard>();

        // Modules
        builder.Services.AddIdentityModule(builder.Configuration);
        builder.Services.AddJobModule(builder.Configuration);

        // Société porteuse des planifications de jobs SYSTÈME (BUG-4b) — remplace le défaut no-op du socle :
        // permet à un opérateur PLATEFORME (sans société courante) de planifier ET consulter les fan-out
        // tous-tenants. Enregistré APRÈS AddJobModule (qui pose le défaut via TryAdd) pour gagner la résolution.
        builder.Services.AddSingleton<Stratum.Modules.Job.Contracts.Services.ISystemScheduleHost, LiakontSystemScheduleHost>();

        builder.Services.AddNotificationModule();

        // Branding d'INSTANCE (BRD01, marque grise — blueprint.md §3.3, F12 §6.1) lié depuis la section
        // "Branding". Valeurs par défaut = marque « Liakont » (aucune donnée client — CLAUDE.md n°7).
        // Consommé par la coquille (BrandingHead, ErpNav), le transport SMTP ci-dessous (expéditeur + pied
        // de page) et l'export de réversibilité (Archive lit sa propre tranche de la même section).
        builder.Services.Configure<BrandingOptions>(builder.Configuration.GetSection(BrandingOptions.SectionName));

        // Transport SMTP réel (ADR-0018, SUP03) EN REMPLACEMENT du StubEmailTransport du socle (vendored, NON
        // modifié). Config d'instance liée depuis la section "Smtp" (gabarit vide dans appsettings.json ; mot de
        // passe injecté au déploiement, jamais en clair — CLAUDE.md n°10). Désactivé/non configuré = no-op
        // journalisé (pas de retry infini). Consommé par EmailSendJobHandler au moment de la livraison.
        builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));
        builder.Services.Replace(ServiceDescriptor.Scoped<IEmailTransport, SmtpEmailTransport>());

        // Libellé FR fourni à l'enregistrement (FIX211) : l'admin des planifications affiche ce libellé, jamais
        // le FullName .NET (clé technique stockée). Surfacé par IJobTypeCatalog.
        builder.Services.AddJobHandler<EmailSendJobPayload, EmailSendJobHandler>("Envoi d'e-mail");
        builder.Services.AddJobHandler<DeliveryRetryJobPayload, DeliveryRetryJobHandler>("Relance d'envoi d'e-mail");
        builder.Services.AddAuditModule();
        builder.Services.AddTenantSettingsModule();
        builder.Services.AddIngestionModule();
        builder.Services.AddTvaMappingModule();

        // DocumentApproval (SIG04, ADR-0028) : workflow de validation de document GÉNÉRIQUE (cœur réutilisable
        // du lot signature) — agrégat à machine fermée par purpose, slots N-parties, journal append-only,
        // règle de gate. Le Host enregistre la persistance (migrations du schéma documentapproval + UoW +
        // requêtes + port de commande générique). Aucun port par purpose ni job ici (SIG06/SIG07) ; aucune
        // logique fiscale (CLAUDE.md n°2). ⚠️ ENREGISTRÉ AVANT Mandats : depuis SIG05, la migration de bascule
        // du module Mandats (self-billing → documentapproval) ÉCRIT dans le schéma documentapproval ; ses tables
        // (V001-V004) doivent donc exister AVANT. La garantie tient des DEUX côtés : (1) par le NOM de script,
        // « ...DocumentApproval...Migrations.V004 » trie avant « ...Mandats...Migrations.V010 » (ordre lexical
        // des ressources embarquées) ; (2) cet enregistrement de DocumentApproval AVANT Mandats (défense en
        // profondeur, et c'est l'ordre des fixtures de test). La migration V010 est exercée sur données réelles
        // par SelfBilledAcceptanceMigrationV010Tests.
        builder.Services.AddDocumentApprovalModule();

        // Mandats (F15 §2, ADR-0022) : registre des mandants + cycle de vie des mandats (autofacturation
        // 389) + acceptation 389 PROJETÉE via DocumentApproval (SIG05 : machine + journal document_approval_log
        // délégués au module générique ; le module garde le port ISelfBilledGate, l'allocation BT-1 et la
        // companion fiscale allocated_number). Les handlers de bascule tacite arrivent par MND04.
        builder.Services.AddMandatsModule();

        // Validation (F04) : expose IValidationService à la frontière Contracts (consommé par le pipeline,
        // PIP01b). Les règles métier (IDocumentRule, VAL02-VAL05) s'enregistrent avec leurs items ; sans
        // règle, la validation passe (aucune anomalie).
        builder.Services.AddValidationModule();

        // Documents après Ingestion (ordre sans impact sur la correction : Ingestion utilise TryAdd,
        // Documents utilise Replace — la vraie implémentation gagne toujours). Conservé après Ingestion
        // pour la lisibilité du registre.
        builder.Services.AddDocumentsModule();

        // Payments (F09, TRK04) : encaissements bruts + agrégats jour × taux + piste d'audit append-only.
        // Modèle et persistance uniquement ; l'agrégation et la transmission arrivent avec le pipeline (PIP03).
        builder.Services.AddPaymentsModule();

        // Archive (TRK05) après Documents : alimente documents.archive_entries (table créée par TRK01).
        // Store par défaut = FileSystem (appliance) ; une instance choisit S3 via AddS3ArchiveStore (ADR-0009).
        builder.Services.AddArchiveModule(builder.Configuration);

        // Job d'ancrage temporel quotidien du coffre (TRK06) : le handler de fan-out est résolu par le
        // module Job (même mécanique que EmailSend/DeliveryRetry). La PLANIFICATION (cron quotidien) est
        // créée par l'opérateur via l'admin des schedules (job.schedules), comme tout job récurrent de la
        // plateforme — la fréquence et l'activation relèvent du déploiement (ADR-0011).
        builder.Services.AddJobHandler<DailyAnchoringTrigger, DailyAnchoringFanOutHandler>("Ancrage quotidien du coffre d'archive");

        // Bascule tacite des acceptations d'auto-factures 389 (MND04, ADR-0024 §4) : le handler de fan-out
        // (gabarit DailyAnchoring/SOL06) bascule PendingAcceptance → TacitlyAccepted pour les documents sous
        // mandat écrit dont l'échéance (DeadlineUtc) est échue. La CADENCE n'est PAS fixée par la spec (F15
        // §2.3 ne fixe que l'échéance par document) : aucune n'est inventée → entrée SystemJobDefinitions de
        // classe DeploymentCadence (cron null, NON amorcée) pour que le diagnostic de démarrage signale un job
        // jamais planifié (RDL07/A6-cons-2) ; la planification reste un geste opérateur (admin des schedules).
        builder.Services.AddJobHandler<SelfBilledAcceptanceTacitTrigger, SelfBilledAcceptanceTacitFanOutHandler>(
            "Bascule tacite des acceptations d'auto-factures");

        // Staging du contenu pivot (PIP00, ADR-0014) : la plateforme détient DURABLEMENT le pivot dès
        // l'intake (l'agent redevient un filet de sécurité). Magasin transitoire purgeable, chiffré au
        // repos, tenant-scopé — distinct du coffre WORM. La sonde de présence WORM est l'adaptateur du
        // coffre concret câblé au composition root (seul endroit autorisé à référencer IArchiveStore hors
        // du module Archive) : elle subordonne la purge du staging à l'écriture WORM effective.
        builder.Services.AddStagingModule(builder.Configuration);
        builder.Services.AddScoped<IArchivedDocumentProbe, ArchiveStoreArchivedDocumentProbe>();

        // Emplacement du magasin de staging : repli STABLE hors arbre de build (FIX07b) quand aucune racine
        // d'instance n'est configurée (Staging:Storage:FileSystem:RootPath). Voir StagingHostRegistration —
        // remplace le repli bin/ du module, cause de la perte de contenu (documents zombies).
        builder.Services.AddStableStagingRoot(builder.Environment.ContentRootPath);

        // Trace de support du Factur-X transmis (FX06, F16 §7) : store DÉDIÉ, tenant-scopé, chiffré au repos,
        // à rétention courte (proposition 90 j configurable) et PURGEABLE — distinct par construction de la
        // piste d'audit append-only (documents.document_events) et du coffre WORM probant. Le handler de purge
        // fait le fan-out par tenant (TenantJobRunner, SOL06) ; sa PLANIFICATION (cron) reste un geste opérateur
        // via l'admin des planifications — aucune cadence inventée (housekeeping de rétention courte = cadence de
        // déploiement) : entrée SystemJobDefinitions de classe DeploymentCadence (cron null, non amorcée) pour
        // que le diagnostic de démarrage signale un job jamais planifié (RDL07/A6-cons-2). L'ÉCRITURE de la trace
        // (au moment de la transmission) est câblée par FX07.
        builder.Services.AddSupportTraceModule(builder.Configuration);
        builder.Services.AddJobHandler<SupportTracePurgeTrigger, SupportTracePurgeFanOutHandler>("Purge de la trace de support du Factur-X");

        // Reconciliation (TRK07) après Archive : rapproche les PDF du pool non lié des documents émis et
        // ajoute le PDF réconcilié au paquet d'archive en addendum (consomme IArchiveService). Le job
        // système fait le fan-out de la passe sur tous les tenants via le TenantJobRunner (SOL06).
        builder.Services.AddReconciliationModule();
        builder.Services.AddJobHandler<ReconciliationFanOutJobPayload, ReconciliationFanOutJobHandler>("Rapprochement des PDF (réconciliation)");

        // Supervision (SUP01a, F12 §5) : moteur d'alertes + dead-man's-switch. Le handler du job SYSTÈME fait
        // le fan-out de l'évaluation sur TOUS les tenants via le TenantJobRunner (SOL06) — c'est la plateforme
        // qui détecte l'absence (panne silencieuse), pas l'agent qui signale sa présence. AUCUNE règle concrète
        // n'est enregistrée ici (SUP01b livre les 8 règles) : sans règle, l'évaluation ne produit aucune alerte.
        // La PLANIFICATION (cron toutes les 15 min, F12 §5.1) est créée par l'opérateur via l'admin des
        // schedules (job.schedules), comme l'ancrage quotidien (TRK06) — la fréquence relève du déploiement.
        builder.Services.AddSupervisionModule();
        builder.Services.AddJobHandler<SupervisionEvaluationTrigger, SupervisionEvaluationFanOutHandler>("Évaluation de la supervision");

        // Notifications email des alertes (SUP03, F12 §5.3) : destinataires/options d'instance liés depuis la
        // section "Supervision:Notifications". Le récapitulatif quotidien (digest, OPTIONNEL, défaut désactivé)
        // est un job SYSTÈME dont le handler fait le fan-out par tenant — sa PLANIFICATION (cron quotidien) est
        // créée par l'opérateur via l'admin des schedules, comme l'évaluation (15 min) et l'ancrage TRK06.
        builder.Services.Configure<SupervisionNotificationOptions>(
            builder.Configuration.GetSection(SupervisionNotificationOptions.SectionName));
        builder.Services.AddJobHandler<SupervisionDigestTrigger, SupervisionDigestFanOutHandler>("Récapitulatif quotidien de supervision");

        // Méta-supervision de FLOTTE (OPS04, F12 §6) : le niveau AU-DESSUS des tenants — IT Innovations
        // supervise les INSTANCES (le module Supervision, lui, supervise les tenants d'UNE instance). Une
        // instance peut tenir le rôle CENTRAL (reçoit les heartbeats, dashboard de flotte, notification de
        // mise à jour) et/ou REPORTING (envoie sa télémétrie technique au central). Tout est DÉSACTIVÉ par
        // défaut (section "FleetSupervision", gabarit vide dans appsettings.json) — opt-in par déploiement.
        // La télémétrie est strictement technique : AUCUNE donnée métier d'éditeur (cloisonnement, OPS04).
        // Les job handlers sont enregistrés ici (comme supervision/ancrage) ; leur PLANIFICATION (cron) est
        // un geste opérateur via l'admin des planifications.
        builder.Services.Configure<FleetSupervisionOptions>(
            builder.Configuration.GetSection(FleetSupervisionOptions.SectionName));
        builder.Services.AddFleetSupervisionModule();
        builder.Services.AddJobHandler<InstanceHeartbeatTrigger, InstanceHeartbeatSendHandler>("Télémétrie d'instance (méta-supervision)");
        builder.Services.AddJobHandler<FleetUpdateNotificationTrigger, FleetUpdateNotificationHandler>("Notification de mise à jour de la flotte");

        // Transmission (PAA01) : registre de types des plug-ins PA. Aucun plug-in n'est référencé ici
        // (le module ne connaît AUCUNE PA concrète — CLAUDE.md n°6) ; chaque plug-in PA (PAA02 Fake,
        // PAB B2Brouter, PAS Super PDP) ajoutera sa propre IPaClientFactory en singleton et le registre
        // la découvrira. Le pipeline (PIP) consomme IPaClientRegistry pour résoudre la PA du tenant.
        builder.Services.AddTransmissionModule();

        // Plug-ins PA câblés au COMPOSITION ROOT (FIX01d) — seul endroit autorisé à référencer un
        // plug-in PA concret (CLAUDE.md n°6/14). En Development (ou via PaClients:Fake:Enabled), le
        // plug-in factice (PAA02) est ajouté pour rendre l'envoi exerçable de bout en bout sans PA
        // réelle (bug-inbox « Fake jamais câblé » : sans lui le registre ci-dessus ne résout rien) ;
        // en production il reste absent. Le registre le découvre et le résout par PaType (CLAUDE.md n°8).
        builder.Services.AddConfiguredPaClients(builder.Environment, builder.Configuration);

        // Génération Factur-X (FX02-FX04) : enregistre le port IFacturXBuilder (sérialiseur CII maison +
        // scellement PDF/A-3 QuestPDF confiné à FacturX.Infrastructure, INV-FX-1). La décision de générer
        // reste au pipeline appelant (FacturX ne consulte aucune PaCapabilities — ADR-0023 INV-FX-4).
        builder.Services.AddFacturXModule(builder.Configuration);

        // FX07 (F16 §6.1) : le plug-in PA GÉNÉRIQUE (Essentiel) et ses canaux de livraison Host sont câblés
        // ICI, EN MÊME TEMPS que la génération à l'étape Sending — au moment où le pipeline sait nourrir le
        // plug-in (extension du contrat IPaClient via PaSendContext + génération du Factur-X). Le pont
        // IFacturXArtifactBuilder → IFacturXBuilder (composition root) laisse le pipeline générer « derrière
        // IFacturXBuilder » SANS franchir la frontière Contracts-only (module-rules §3) : seul le Host
        // référence à la fois Transmission.Contracts et le module FacturX. Un compte « Generique » actif
        // devient ainsi transmissible de bout en bout (génération → transmission → journal + trace support).
        builder.Services.AddSingleton<IFacturXArtifactBuilder, FacturXArtifactBuilder>();
        builder.Services.AddGeneriquePaDelivery();

        // Plug-in PA Super PDP (F14, OAuth2 client_credentials) câblé au COMPOSITION ROOT — seul endroit
        // autorisé à référencer un plug-in PA concret (CLAUDE.md n°6/14). Le résolveur de compte (déchiffrement
        // des secrets OAuth2 par tenant via ISecretProtector + lecture de pa_accounts) est fourni par le Host :
        // le plug-in ne voit pas le coffre. Resolver AVANT la fabrique (AddSuperPdpPaClient en dépend, comme
        // Yousign). NON gardé par l'environnement (PA réelle, contrairement au Fake) : le « sandbox-only » est
        // imposé au runtime par SuperPdpAccountConfig.BaseUrl, qui lève en Production tant que PAS03 n'a pas
        // confirmé l'URL (F14 §12 O1) — on bloque plutôt que d'envoyer faux (CLAUDE.md n°3).
        builder.Services.TryAddSingleton<ISuperPdpAccountResolver, SuperPdpAccountResolver>();
        builder.Services.AddSuperPdpPaClient();

        // Plug-in PA Chorus Pro (F18, OAuth2WithTechnicalAccount — dépôt d'un Factur-X scellé via PISTE) câblé au
        // COMPOSITION ROOT — seul endroit autorisé à référencer un plug-in PA concret (CLAUDE.md n°6/14). Le
        // résolveur de compte (déchiffrement des secrets PISTE + mot de passe du compte technique par tenant via
        // ISecretProtector + lecture de pa_accounts ; URLs verrouillées au raccordement portées par
        // account_identifiers, F18 §3.3) est fourni par le Host — le plug-in ne voit pas le coffre. Resolver
        // AVANT la fabrique (AddChorusProPaClient en dépend, comme Super PDP/Yousign). Câblé ICI et NON dans
        // PaClientBootstrap.AddConfiguredPaClients (qui ne câble que le Fake). Le « bloquer plutôt qu'envoyer
        // sans auth » est imposé par le résolveur (fail-closed) et le constructeur de ChorusProAccountConfig.
        builder.Services.AddChorusProPaDelivery();

        // Signature (SIG03, ADR-0027) : registre de types des fournisseurs de signature. Aucun plug-in
        // n'est référencé ici (le module ne connaît AUCUN fournisseur concret — CLAUDE.md n°6) ; chaque
        // plug-in (Yousign = SIG07, Wacom = SIG08) ajoutera sa propre ISignatureProviderFactory en singleton
        // au composition root et le registre la découvrira. La signature est OPTIONNELLE : le registre se
        // construit vide sans erreur (défaut Recorded) ; les fournisseurs CONFIGURÉS mais non câblés sont
        // détectés au démarrage (ValidateSignatureProviderConfiguration, dans InitializeDataAsync).
        builder.Services.AddSignatureModule();

        // Plug-in de signature à distance Yousign (SIG07, ADR-0029) câblé au COMPOSITION ROOT — seul endroit
        // autorisé à référencer un plug-in concret (CLAUDE.md n°6/14). Le résolveur de compte (déchiffrement
        // des secrets par tenant via ISecretProtector) est fourni par le Host (le plug-in ne voit pas le
        // coffre) ; AddYousignSignatureProvider enregistre le client HTTP anti-SSRF + la fabrique (singleton),
        // que le registre du module Signature découvre par son type. Le drain WORM (job système fan-out par
        // tenant via TenantJobRunner) est enregistré comme les autres jobs (DailyAnchoring/Supervision).
        // NB : AddDocumentApprovalModule() est enregistré plus haut (avant Mandats) depuis SIG05 — ordre des
        // migrations DbUp (Mandats écrit dans le schéma documentapproval).
        builder.Services.TryAddSingleton<IYousignAccountResolver, YousignAccountResolver>();
        builder.Services.AddYousignSignatureProvider();
        builder.Services.AddJobHandler<SignatureWebhookDrainTrigger, SignatureWebhookDrainFanOutHandler>(
            "Drain des webhooks de signature (rapatriement WORM)");

        // Pipeline (PIP01a — fondations) : lecteur canonique du contenu stagé + journal d'exécutions
        // (pipeline.run_logs) + points d'entrée Contracts consommés par CHECK/SEND/SYNC. AUCUN comportement
        // de pipeline ici (PIP01b-d) ; le pipeline ne référence aucune PA concrète (CLAUDE.md n°6).
        builder.Services.AddPipelineModule();

        // RDL06 — les 4 fan-out SYSTÈME récurrents du pipeline (SendAll/SyncAll/AggregatePaymentsAll/
        // RectifyReportsAll) sont câblés ICI via AddJobHandler (l'extension vit dans le module Job, que seul le
        // Host référence) : c'est AddJobHandler qui pose la JobHandlerRegistration singleton vue par le
        // JobHandlerResolver (dispatch) et le JobTypeCatalog (planification). Leur PLANIFICATION (cron) reste un
        // geste opérateur via l'admin des schedules, comme l'ancrage TRK06 et la supervision.
        builder.Services.AddPipelineSystemJobHandlers();

        // SEND déclenché par une ACTION de console (API02a, ADR-0016) : handler SYSTÈME du déclencheur
        // MONO-TENANT SendTenantTrigger. Enregistré au composition root (comme DailyAnchoring/Supervision)
        // pour exposer sa JobHandlerRegistration au JobHandlerResolver — c'est lui que le JobWorker résout
        // quand une action « send / send-all / runs-trigger » publie le déclencheur sur la queue système.
        // À la différence du SendAllTrigger planifié (fan-out tous-tenants, cron), il rétablit le SEUL tenant
        // de l'opérateur via ITenantScopeFactory.Create — aucune itération multi-tenant (CLAUDE.md n°9).
        builder.Services.AddJobHandler<SendTenantTrigger, SendTenantFanInHandler>("Envoi des documents (tenant courant)");

        // Stockage des PDF reçus (PIV04) : chemin racine = PARAMÉTRAGE de déploiement (jamais en dur,
        // CLAUDE.md n°7). Lié depuis la config ; à défaut, repli sous le content root de l'instance.
        builder.Services.Configure<IngestionStorageOptions>(
            builder.Configuration.GetSection(IngestionStorageOptions.SectionName));
        builder.Services.PostConfigure<IngestionStorageOptions>(opts =>
        {
            if (string.IsNullOrWhiteSpace(opts.PdfRootPath))
            {
                opts.PdfRootPath = System.IO.Path.Combine(builder.Environment.ContentRootPath, "App_Data", "ingestion-pdf");
            }
        });

        // Rate limiting de l'API agent (F12 §3.3) — défense en profondeur, PROTECTION ANTI-FLOOD : le
        // vrai rempart contre le brute force est la clé cryptographique (secret 256 bits) + la
        // révocation ; un secret ne se devine pas par volume de requêtes. La fenêtre fixe par IP est
        // donc dimensionnée GÉNÉREUSEMENT pour ne jamais rejeter du trafic légitime (heartbeats,
        // configuration), même agrégé derrière un proxy — un 429 sur un heartbeat légitime
        // déclencherait un FAUX POSITIF du dead-man's switch (F12 §5).
        // NOTE déploiement : derrière le reverse proxy de l'appliance (F12 §6.2/6.6), RemoteIpAddress
        // est l'IP du proxy tant que ForwardedHeaders n'est pas configuré → la fenêtre dégrade en
        // throttle GLOBAL plutôt que par-IP. Activer ForwardedHeaders relève d'OPS et EXIGE une liste
        // de proxys de confiance (sinon X-Forwarded-For est usurpable et la limite contournable).
        // NOTE PIV04 : l'ingestion de documents par lots (gros débit) ajoutera SA PROPRE policy
        // dimensionnée pour le débit, plutôt que de partager ce quota anti-flood.
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(AgentApiEndpoints.RateLimiterPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 600,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));

            // Policy d'INGESTION (PIV04) : le drainage d'un backlog pousse des lots en rafale ; la
            // limite est dimensionnée pour le DÉBIT (et non l'anti-flood) afin de ne jamais rejeter un
            // drainage légitime. Le vrai rempart anti-brute-force reste la clé cryptographique + le
            // filtre d'authentification (déjà appliqués au groupe). Même réserve « derrière proxy »
            // que la policy anti-flood : sans ForwardedHeaders, la fenêtre dégrade en throttle global.
            options.AddPolicy(AgentApiEndpoints.IngestionRateLimiterPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 1200,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));

            // Heartbeat de flotte (OPS04) : anti-flood par IP de l'endpoint d'ingestion central. Le heartbeat
            // est rare (un par instance toutes les quelques minutes) ; la fenêtre est dimensionnée largement
            // pour ne jamais rejeter une instance légitime, tout en bornant le brute-force de la clé + les
            // écritures. Même réserve « derrière proxy » que les policies agent (sans ForwardedHeaders, la
            // fenêtre dégrade en throttle global).
            options.AddPolicy(FleetApiEndpoints.RateLimiterPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 120,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));

            // Webhook de signature (SIG07, ADR-0029) : endpoint anonyme (la vraie garde est le HMAC par
            // tenant). Fenêtre par IP anti-flood, dimensionnée généreusement (un provider peut émettre des
            // rafales légitimes) — borne le brute-force/inondation sans rejeter du trafic valide.
            options.AddPolicy(SignatureWebhookEndpoints.RateLimiterPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 300,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
        });

        // Liaison JSON du contrat agent (F12 §3) : le contrat émet ses énumérations par leur NOM
        // (cf. CanonicalJson / fixtures contrat-v1). Sans convertisseur string→enum, System.Text.Json
        // attend un nombre et rejette un lot au format documenté en 400 au model-binding (requête) et
        // émet le statut de réponse en nombre. Scopé aux trois enums du contrat (deux en requête, un en
        // réponse — voir AgentApiJson) pour ne pas toucher le format des autres enums. RDL04 ajoute le
        // rejet strict des membres inconnus sur les DTOs du contrat (intégrité du hash N+1→N).
        builder.Services.ConfigureHttpJsonOptions(options => AgentApiJson.ConfigureContractBinding(options.SerializerOptions));

        // Le module ERP Party n'est pas vendoré (seul Party.Contracts — décision D1). Identity
        // dépend de IPartyQueries par injection ; Liakont ne lie pas ses utilisateurs à des Party
        // ERP (PartyId toujours null). Shim no-op pour satisfaire la validation du graphe DI.
        builder.Services.AddScoped<Stratum.Modules.Party.Contracts.Queries.IPartyQueries,
            Liakont.Host.Compatibility.NullPartyQueries>();

        // Real claims-based permission service (replaces the socle's test/no-op default).
        builder.Services.AddScoped<IPermissionService, ClaimsPermissionService>();

        // API versioning
        builder.Services.AddApiVersioning(opt =>
        {
            opt.DefaultApiVersion = new ApiVersion(1, 0);
            opt.ReportApiVersions = true;
            opt.ApiVersionReader = new UrlSegmentApiVersionReader();
        });

        // OpenAPI documentation (Development and Test only)
        if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test"))
        {
            builder.Services.AddOpenApi("v1", options =>
            {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Liakont API",
                    Version = "v1",
                    Description = "Liakont — passerelle de conformité e-invoicing (API plateforme)",
                };

                var components = document.Components ?? new OpenApiComponents();
                document.Components = components;
                components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "Enter your JWT token",
                };

                return Task.CompletedTask;
            });

            options.AddOperationTransformer((operation, context, _) =>
            {
                // Apply Bearer auth requirement only to endpoints that require authorization
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                var allowAnonymous = metadata.OfType<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>().Any();
                if (!allowAnonymous)
                {
                    var schemeRef = new OpenApiSecuritySchemeReference("Bearer");
                    operation.Security ??= [];
                    operation.Security.Add(new OpenApiSecurityRequirement
                    {
                        [schemeRef] = new List<string>(),
                    });
                }

                return Task.CompletedTask;
            });
        });
        }

        // Settings
        builder.Services.Configure<KeycloakSettings>(builder.Configuration.GetSection("Keycloak"));
        builder.Services.Configure<AdminSeedOptions>(builder.Configuration.GetSection("AdminSeed"));

        // Actor context
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IActorContextAccessor, HttpActorContextAccessor>();

        // Authorization handler
        builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        // MediatR pipeline behaviors (order: actor → tenant → action pipeline → entity changed)
        builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ActorContextBehavior<,>));
        builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TenantPropagationBehavior<,>));
        builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ActionPipelineBehavior<,>));
        builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(EntityChangedBehavior<,>));

        // Authentication & Authorization — consommées DERRIÈRE l'abstraction d'IdP (décision D10).
        // Keycloak est UNE implémentation ; une alternative in-process (ex. OpenIddict) se branche
        // ici sans toucher au reste du Host. Aucun appel IdP-spécifique hors de la couche d'auth.
        var keycloakSettings = builder.Configuration.GetSection("Keycloak").Get<KeycloakSettings>()
            ?? throw new InvalidOperationException("Keycloak configuration section is required. Configure Keycloak:Authority and Keycloak:ClientId.");

        // Sélecteur d'IdP (décision D10) : « Identity:Provider » pilote l'implémentation,
        // défaut Keycloak. Une alternative in-process (ex. OpenIddict) se branche ici.
        var providerName = builder.Configuration["Identity:Provider"];
        IIdentityProviderAuthenticator idp = SelectIdentityProvider(providerName, keycloakSettings);
        idp.ValidateConfiguration();
        idp.ConfigureAuthentication(builder);

        builder.Services.AddAuthorization(options =>
        {
            // VolunteerPolicy = access gate (user has volunteer role).
            // Permission restriction for volunteer-only users is enforced separately
            // by VolunteerAuthorizationHandler on PermissionRequirement checks.
            options.AddPolicy(VolunteerPermissions.PolicyName, policy =>
                policy.RequireRole(StratumRoles.Volunteer));
        });
        builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        builder.Services.AddScoped<IAuthorizationHandler, VolunteerAuthorizationHandler>();

        // Provisioning d'utilisateur de tenant (OPS03 lot A) : abstraction produit IdP-agnostique,
        // implémentation Keycloak dans la couche d'auth (seul endroit autorisé — décision D10).
        builder.Services.AddScoped<Security.Abstractions.ITenantUserProvisioningService, Security.Keycloak.KeycloakTenantUserProvisioner>();

        // Gestion des utilisateurs de tenant depuis la console (RB4 inc1 : lister + réinitialiser le
        // mot de passe). Même couche d'auth ; réutilise le client Admin Keycloak (socle, non modifié).
        builder.Services.AddScoped<Security.Abstractions.ITenantUserManagementService, Security.Keycloak.KeycloakTenantUserManagementService>();

        // Application du statut Suspendu (OPS03.4 lot B) : lookup singleton (cache mémoire court,
        // fail-open documenté) consommé par le filtre de push agent, le middleware et le sign-in.
        builder.Services.AddSingleton<MultiTenancy.ITenantSuspensionLookup, MultiTenancy.TenantSuspensionLookup>();

        // Écran « Clients » (OPS03 lot C) : service console d'administration d'instance — liste des
        // tenants (registre système + profils par scope), assistant de création, suspension.
        builder.Services.AddScoped<Clients.IClientConsoleService, Clients.ClientConsoleService>();

        // Navigation providers (sidebar)
        // Scoped (et non Singleton) : la visibilité de « Accueil » dépend du contexte cross-tenant du
        // super-admin (ILiakontConsoleContext scopé + HttpContext de la requête) — RB1.
        builder.Services.AddScoped<INavSectionProvider, HostNavSectionProvider>();

        // FIX209 — assainissement de la nav socle (décision opérateur E5, recette GATE_CONSOLE_WEB run 2) :
        // l'« Annuaire » socle (Agents/Équipes/Délégations — collision avec les « Agents d'extraction » Liakont)
        // et la « Sécurité » socle (Utilisateurs/Rôles — les comptes vivent dans Keycloak sous OIDC ; sort
        // définitif renvoyé à l'ADR IdP / OPS01c) ne sont PLUS câblés dans la nav Liakont. Le socle vendored
        // n'est PAS modifié (CLAUDE.md n°11) : on retire seulement l'ENREGISTREMENT au composition root — les
        // providers Stratum.Modules.Identity.Web.{Identity,Security}NavSectionProvider et leurs routes restent
        // intacts (autres produits Stratum, accès super-admin). La section « Notifications » du socle est, elle,
        // RÉDUITE à « Templates » et gardée par permission via NotificationNavVisibilityFilter (plus bas, SCOPED).

        // Section « Audit » du socle (Journal d'audit + Politiques) filtrée par permission côté Liakont (FIX303,
        // décision opérateur F3, recette GATE_CONSOLE_WEB run 3) : le provider socle déclare les deux entrées
        // inconditionnellement, mais les pages /admin/audit et /admin/audit/policies exigent la permission socle
        // audit.trail.view — jamais accordée par un rôle Liakont (RolePermissionCatalog, matrice §3 immuable) ;
        // seul un super-admin les ouvre. Sans filtre, la section menait à des pages entièrement vides pour tout
        // opérateur normal (même cause que FIX209). SCOPED car la visibilité dépend de l'utilisateur. Le socle
        // vendored n'est PAS modifié (le filtre y délègue) ; la découverte de la ROUTE /admin/audit reste intacte
        // (Routes.razor + discovery d'assembly plus bas) pour le super-admin.
        builder.Services.AddScoped<INavSectionProvider, Liakont.Host.Navigation.AuditNavVisibilityFilter>();

        // Section « Jobs » du socle filtrée par permission côté Liakont (FIX07c) : le provider socle déclare
        // « Planifications » (/admin/jobs) inconditionnellement, mais la page socle exige la permission socle
        // job.view — jamais accordée par un rôle Liakont (RolePermissionCatalog, matrice §3 immuable) ; seul un
        // super-admin l'ouvre. Sans filtre, l'entrée menait à une page entièrement vide pour tout opérateur normal
        // (recette GATE_CONSOLE_WEB). SCOPED car la visibilité dépend de l'utilisateur (comme LiakontNavNodeProvider).
        // Le socle vendored n'est PAS modifié ; la découverte de la ROUTE /admin/jobs (Routes.razor +
        // MapRazorComponents) reste intacte pour le super-admin.
        builder.Services.AddScoped<INavSectionProvider, Liakont.Host.Navigation.JobNavVisibilityFilter>();

        // Section « Notifications » du socle RÉDUITE à « Templates » et gardée par liakont.settings (FIX209,
        // décision E5). Les six autres entrées socle (Règles de routage, Webhooks, Simulation, SLA, Services,
        // Integrations) sont hors périmètre Liakont et plusieurs lèvent « No company context » sous OIDC. SCOPED
        // car la visibilité dépend de l'utilisateur. Le provider socle n'est pas modifié (le filtre y délègue).
        builder.Services.AddScoped<INavSectionProvider, Liakont.Host.Navigation.NotificationNavVisibilityFilter>();

        // Navigation maître Liakont (WEB01, hiérarchisée au lot polish UX/UI) : INavNodeProvider (arbre
        // avec le sous-menu Paramétrage) — consommé par la sidebar ET la palette de recherche. SCOPED car
        // la visibilité dépend du tenant courant (pool PDF → Réconciliation) et du rôle (permissions).
        builder.Services.AddScoped<INavNodeProvider, Liakont.Host.Navigation.LiakontNavNodeProvider>();
        builder.Services.AddScoped<Liakont.Host.Navigation.ILiakontConsoleContext, Liakont.Host.Navigation.LiakontConsoleContext>();

        // Composition en lecture du tableau de bord d'accueil (WEB01) : isole l'assemblage hors de la page.
        builder.Services.AddScoped<Liakont.Host.Dashboard.IDashboardQueries, Liakont.Host.Dashboard.DashboardQueryService>();

        // Composition en lecture de la page Documents (WEB02) : charge tout le périmètre période (boucle
        // sur la liste paginée serveur, aucune troncature) hors de la page.
        builder.Services.AddScoped<Liakont.Host.Documents.IDocumentConsoleQueries, Liakont.Host.Documents.DocumentConsoleQueryService>();

        // Mémoire de circuit des filtres de la liste Documents (issue #33) : le « Retour à la liste »
        // de la fiche détail retrouve la liste telle que l'opérateur l'avait filtrée.
        builder.Services.AddScoped<Liakont.Host.Documents.DocumentsListFilterMemory>();

        // Composition en lecture de la page détail document (WEB03a) : assemble en-tête + piste d'audit +
        // motif de blocage courant + référence d'archive d'un document, hors de la page.
        builder.Services.AddScoped<Liakont.Host.Documents.IDocumentDetailConsoleQueries, Liakont.Host.Documents.DocumentDetailConsoleQueryService>();

        // Actions de résolution terminale du détail document (WEB03c) : traitement manuel hors passerelle
        // et liaison à un document de remplacement. Appelle le port du module (IDocumentLifecycle) + audit,
        // comme les endpoints API02c — aucune logique métier dans la page.
        builder.Services.AddScoped<Liakont.Host.Documents.IDocumentResolutionConsoleService, Liakont.Host.Documents.DocumentResolutionConsoleService>();

        // Composition en écriture de l'onglet Contrôles du détail (WEB03b) : verdict garde-fou B2B/B2C +
        // re-vérification d'un document bloqué (API02b), appelés in-process par la page (tenant-scopé).
        builder.Services.AddScoped<Liakont.Host.Documents.IDocumentControlActions, Liakont.Host.Documents.DocumentControlActionsService>();

        // Actions d'envoi de la page Documents (WEB05) : « Envoyer la sélection », « Tout envoyer » (avec
        // récapitulatif de confirmation) et « Lancer un traitement ». Publie le déclencheur mono-tenant
        // SendTenantTrigger sur la queue SYSTÈME (ADR-0016) + audit, comme les endpoints API02a / runs-trigger —
        // aucun second chemin d'envoi, aucune logique fiscale dans la page (garde liakont.actions, tenant-scopé).
        builder.Services.AddScoped<Liakont.Host.Documents.IDocumentSendActions, Liakont.Host.Documents.DocumentSendActionsService>();

        // Composition de la page console des signatures/validations (SIG10) : lecture (statut + journal + registre
        // des fournisseurs) et écriture (déclencher / enregistrer / contester). Appels in-process tenant-scopés,
        // délégués aux ports génériques DocumentApproval (SIG04/05) + registre Signature (SIG03) — aucune logique
        // métier dans la page (CLAUDE.md n°19), aucune règle fiscale dupliquée.
        builder.Services.AddScoped<Liakont.Host.Signatures.ISignatureConsoleQueries, Liakont.Host.Signatures.SignatureConsoleQueryService>();
        builder.Services.AddScoped<Liakont.Host.Signatures.ISignatureConsoleActions, Liakont.Host.Signatures.SignatureConsoleActionsService>();

        // Composition en lecture de la page Paramétrage du tenant (WEB04b) : assemble /settings + agents
        // et déclenche la vérification d'intégrité du coffre (API03). Isole l'assemblage hors de la page.
        builder.Services.AddScoped<Liakont.Host.Parametrage.IParametrageQueries, Liakont.Host.Parametrage.ParametrageQueryService>();

        // Composition de la page « Paramétrage comptable — Table TVA » (WEB07a) : lecture de la table +
        // journal (API04) et validation humaine (commande TVA05). Isole l'assemblage hors de la page.
        builder.Services.AddScoped<Liakont.Host.TvaMappingTable.ITvaMappingTableQueries, Liakont.Host.TvaMappingTable.TvaMappingTableQueryService>();

        // Composition en lecture de la page Encaissements (WEB06) : assemble les agrégats jour×taux (PIP03a,
        // GET /payments) et l'état du paramétrage pertinent (capacité PA, complétude fiscale — GET /settings).
        builder.Services.AddScoped<Liakont.Host.Payments.IEncaissementsConsoleQueries, Liakont.Host.Payments.EncaissementsConsoleQueryService>();

        // Composition en lecture de la page Émissions e-reporting B2C de la marge (B4) : lit le journal
        // d'émission regroupé par agrégat transmis (pipeline.b2c_margin_emissions) et le projette pour la console.
        builder.Services.AddScoped<Liakont.Host.B2cReporting.IB2cMarginEmissionsConsoleQueries, Liakont.Host.B2cReporting.B2cMarginEmissionsConsoleQueryService>();

        // Composition de la page Réconciliation des PDF (WEB08) : lecture des trois files (TRK07/API04) et
        // actions opérateur (confirmer / rejeter / lier), appelées in-process par la page (tenant-scopé,
        // garde liakont.actions). Isole l'accès au module hors de la page.
        builder.Services.AddScoped<Liakont.Host.Reconciliation.IReconciliationConsoleService, Liakont.Host.Reconciliation.ReconciliationConsoleService>();

        // Composition de la page « Gestion des agents » (WEB09) : lecture du parc (registre système tenant-scopé,
        // indicateur « muet » depuis le seuil de supervision F12 §5.2) et actions de cycle de vie (enregistrement,
        // révocation, rotation) déléguées aux commandes PIV05 in-process — avec parité d'audit avec les endpoints
        // API05 (garde liakont.settings côté page et côté endpoint). Isole l'accès au module hors de la page.
        builder.Services.AddScoped<Liakont.Host.AgentManagement.IAgentManagementConsoleService, Liakont.Host.AgentManagement.AgentManagementConsoleService>();

        // Composition de la page « Comptes plateforme agréée » (FIX01c) : lecture des comptes PA du tenant
        // (sans la clé) + types de plug-ins enregistrés (IPaClientRegistry), et mutations (création, édition,
        // saisie/rotation de clé chiffrée, désactivation) déléguées aux commandes TenantSettings in-process
        // (garde liakont.settings côté page). Isole l'accès au module hors de la page.
        builder.Services.AddScoped<Liakont.Host.PaAccounts.IPaAccountConsoleService, Liakont.Host.PaAccounts.PaAccountConsoleService>();

        // Onboarding de la transmission (FIX201, décision E1) : publie le SIREN / active le tax_report_setting
        // du compte PA actif (appel idempotent EnsureTaxReportSettingAsync, garde liakont.settings, audit).
        // Sans cette action, le diagnostic pré-envoi (F04 §3.1) refuse tout envoi (« Transport not available »).
        builder.Services.AddScoped<Liakont.Host.PaAccounts.IPaPublicationConsoleService, Liakont.Host.PaAccounts.PaPublicationConsoleService>();

        // Composition de la page « Paramétrage › Alertes » (FIX210) : dispositif d'alerte du tenant (règles
        // actives/gelées + seuils effectifs + e-mail opérateur, via le Contract Supervision) et mutations
        // (seuils, contact) déléguées aux commandes TenantSettings (garde liakont.settings côté page).
        builder.Services.AddScoped<Liakont.Host.Alertes.IAlertesConsoleService, Liakont.Host.Alertes.AlertesConsoleService>();

        // Composition de l'écran « Paramétrage › Fiscal » (FIX301) : lecture du paramétrage fiscal du tenant
        // (GetFiscalSettingsQuery) et modification (SetFiscalSettingsCommand, qui valide/parse/journalise),
        // déléguées en in-process (garde liakont.settings côté page). Rend l'e-reporting des encaissements
        // activable sans SQL : sans cet écran, le paramètre restait non renseignable (suspension perpétuelle).
        builder.Services.AddScoped<Liakont.Host.Fiscal.IFiscalConsoleService, Liakont.Host.Fiscal.FiscalConsoleService>();

        // Composition de l'écran « Paramétrage › Mentions de facturation » (BUG-26, F12-A §3.4) : lecture des
        // mentions de facturation du tenant (GetBillingMentionsQuery) et modification (SetBillingMentionsCommand,
        // qui upsert/journalise), déléguées en in-process (garde liakont.settings côté page). Mentions légales FR
        // (BT-20 + BR-FR-05) portées sur la facture B2B — renseignables sans SQL.
        builder.Services.AddScoped<Liakont.Host.BillingMentions.IBillingMentionsConsoleService, Liakont.Host.BillingMentions.BillingMentionsConsoleService>();

        // Témoin de vie du dead-man's-switch (FIX210, F12 §5.1) : lit les exécutions du job SYSTÈME
        // d'évaluation (base système) via un scope SANS tenant ambiant. Horloge partagée (TimeProvider) pour
        // un « en retard » déterministe en test.
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddScoped<Liakont.Host.Supervision.ISupervisionLivenessProvider, Liakont.Host.Supervision.SupervisionLivenessProvider>();

        // Pré-chargement déterministe de l'état de console à l'ouverture du circuit (avant rendu de la nav).
        builder.Services.AddScoped<Liakont.Host.Navigation.LiakontConsoleCircuitHandler>();
        builder.Services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler>(
            sp => sp.GetRequiredService<Liakont.Host.Navigation.LiakontConsoleCircuitHandler>());

        // Fuseau du NAVIGATEUR (RB6) : enregistré par AddCommonUI() (socle Stratum.Common.UI) avec les autres
        // services scopés des composants socle — l'hôte n'a rien à enregistrer ici (auto-suffisance du socle).

        // Blazor Server-Side Rendering
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents(options =>
            {
                if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test"))
                {
                    options.DetailedErrors = true;
                }
            });
        builder.Services.AddSignalR(options =>
        {
            // Allow JS interop responses up to 10 MB (screenshots, screen recordings).
            options.MaximumReceiveMessageSize = 10 * 1024 * 1024;
        });
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider,
            Microsoft.AspNetCore.Components.Server.ServerAuthenticationStateProvider>();
    }

    /// <summary>
    /// Runs one-time initialisation (database migration and admin seed).
    /// Call after <see cref="WebApplication.Build"/>.
    /// </summary>
    public static async Task InitializeDataAsync(WebApplication app)
    {
        // Validation au démarrage des fournisseurs de signature CONFIGURÉS (SIG03, ADR-0027 §4). Sur le
        // modèle de la validation de l'abstraction IdP, mais — différence essentielle — la signature est
        // OPTIONNELLE : ne bloque QUE pour un fournisseur déclaré dans Signature:EnabledProviders mais non
        // câblé ; l'absence de tout fournisseur n'est jamais une erreur (un tenant Recorded démarre sans
        // plug-in — INV-SIGPROV-6).
        ValidateSignatureProviderConfiguration(app.Configuration, app.Services);

        // Validation au démarrage du profil de déploiement (RDF11, redline ADR fondateurs RL-IDP-8) :
        // le flag Keycloak:DedicatedRealmPerTenant choisit SaaS partagé (défaut) vs dédié mono-tenant.
        // Fail-closed : en dédié sans API Admin Keycloak, le provisioning realm-par-tenant ne peut pas
        // tourner → on bloque plutôt que d'activer une capacité latente sans son pré-requis (avenant
        // RDF11 de l'ADR-0021 ; capacité dédiée hors périmètre INV-0021-*).
        ValidateDedicatedRealmConfiguration(app.Configuration);

        // Signale l'activation du puits factice hors Development avant toute initialisation,
        // pour qu'un opérateur ayant posé PaClients:Fake:Enabled=true par erreur le voie immédiatement.
        app.WarnIfFakePaClientForcedOutsideDevelopment();

        app.MigrateDatabase();

        // Seed dev du tenant par défaut (Development uniquement, section DevTenantSeed) — AVANT la
        // migration des tenants existants pour que le tenant amorcé soit migré dans la même passe.
        await app.SeedDevTenantAsync();
        await MigrateExistingTenantsAsync(app);

        // Amorce le profil de paramétrage du tenant de dev APRÈS sa migration (le schéma tenantsettings
        // n'existe qu'à ce stade) : sans profil, le CHECK suspend tout document (CFG02). Development only,
        // non fatal.
        await app.SeedDevTenantProfileAsync();

        // Onboarding de la transmission du tenant de dev (FIX201, décision E1 point 2) : publie le SIREN /
        // active le tax_report_setting du compte PA Fake, APRÈS l'amorçage du profil. Sans cette étape, le
        // diagnostic pré-envoi refuse tout envoi (« Transport not available »). Rejouée à chaque démarrage
        // (l'état du Fake vit en mémoire). Development only, non fatal.
        await app.SeedDevTenantPublicationAsync();

        await app.Services.SeedAdminUserAsync();
        await SeedRealmRegistryFromDatabaseAsync(app);

        // Diagnostic d'expérience de dev (FIX07a) : avertit si le realm Keycloak de dev est joignable
        // mais périmé (import sauté à cause d'un volume résiduel). Development uniquement, best-effort.
        await app.WarnIfDevRealmStaleAsync();

        // Amorçage DEV des planifications des jobs SYSTÈME (FIX203b) : supervision (15 min, F12 §5.1) et
        // ancrage quotidien du coffre (TRK06, ADR-0011). Sans elles, job.schedules reste VIDE → supervision
        // morte en silence + coffre jamais ancré (recette run 2). Development uniquement, create-only, best-effort.
        await app.SeedDevJobSchedulesAsync();

        // Diagnostic (dev ET prod, FIX203b) : avertit si un job SYSTÈME attendu n'a aucune planification
        // active. En prod la planification est un geste OPS (README) ; ce warning évite la panne silencieuse.
        await app.WarnIfSystemJobsUnscheduledAsync();
    }

    /// <summary>Configures the HTTP pipeline and maps all endpoints.</summary>
    public static void ConfigureMiddleware(WebApplication app)
    {
        // Reverse proxy de l'appliance (Caddy, F12 §6.2/6.6) — DOIT être le tout premier middleware :
        // honorer X-Forwarded-* pour que le SCHÉMA et l'HÔTE publics soient corrects (redirect_uri
        // OIDC, cookies marqués Secure) et que la fenêtre anti-flood par IP (AddRateLimiter) retrouve
        // l'IP cliente réelle au lieu de celle du proxy. Confiance STRICTEMENT bornée aux réseaux/proxys
        // déclarés (voir ForwardedHeadersConfiguration). Désactivé par défaut → accès direct inchangé.
        var forwardedOptions = ForwardedHeadersConfiguration.Build(app.Configuration.GetSection("ForwardedHeaders"));
        if (forwardedOptions is not null)
        {
            app.UseForwardedHeaders(forwardedOptions);
        }

        app.UseStratumErrorHandling();
        var contentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
        contentTypeProvider.Mappings[".module"] = "application/javascript";
        app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = contentTypeProvider });

        app.UseAuthentication();
        app.UseStratumMultiTenancy();

        // Application du statut SUSPENDU d'un tenant (OPS03.4 lot B) : APRÈS l'authentification et la
        // résolution du tenant, AVANT l'autorisation — sessions ouvertes et API Bearer ; le refus au
        // sign-in OIDC est porté par la couche d'auth, le refus de push agent par le filtre d'endpoint.
        app.UseMiddleware<Liakont.Host.MultiTenancy.TenantSuspensionMiddleware>();

        // Cross-check d'isolation par claim company_id (ADR-0021 §2b, item RLM03, INV-0021-4) : middleware
        // GLOBAL *fail-closed* — pour TOUTE requête authentifiée d'un utilisateur de tenant, exige un claim
        // company_id résolvant (outbox.tenants) au tenant servi ; absence, divergence ou indice client
        // contredisant le jeton ⇒ 403. Super-admin exempté, chemin agent (X-Agent-Key) hors périmètre.
        // APRÈS auth + résolution du tenant, AVANT l'autorisation — la défense en profondeur qui remplace la
        // frontière cryptographique par-realm (le contrôle PRIMAIRE reste le scoping métier, CLAUDE.md n°9).
        app.UseMiddleware<Liakont.Host.MultiTenancy.TenantCompanyCrossCheckMiddleware>();

        // Localisation APRÈS l'authentification ET la résolution du tenant : la préférence Language
        // PERSISTÉE de l'utilisateur (base = source de vérité — décision opérateur 2026-06-10,
        // bug-inbox console-web) prime sur le cookie. Le provider lit les claims du principal
        // authentifié, et identity.user_preferences est une table PAR TENANT : la lecture passe par
        // TenantScopedConnectionFactory, qui exige un tenant déjà résolu pour router vers la bonne
        // base (en database-per-tenant, lire avant la résolution retomberait silencieusement sur la
        // base système, où la préférence n'existe pas). Le cookie .AspNetCore.Culture ne sert que de
        // repli pour les requêtes anonymes (ex. /login).
        app.UseRequestLocalization(new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture(SupportedCultures.DefaultCulture),
            SupportedCultures = SupportedCultures.All,
            SupportedUICultures = SupportedCultures.All,
            RequestCultureProviders =
            [
                new Liakont.Host.Localization.PersistedLanguageRequestCultureProvider(),
                new CookieRequestCultureProvider { CookieName = ".AspNetCore.Culture" },
            ],
        });

        app.UseAuthorization();
        app.UseAntiforgery();
        app.UseRateLimiter();

        // OpenAPI & Swagger UI (Development and Test only)
        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Test"))
        {
            app.MapOpenApi();

            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/openapi/v1.json", "Liakont API v1");
            });
        }

        app.MapStratumHealthChecks();

        // OIDC auth endpoints — only registered when Keycloak login is active
        // (Authority configured AND UseKeycloak=true), matching the Login.razor guard.
        var kcConfig = app.Configuration.GetSection("Keycloak").Get<KeycloakSettings>();
        if (kcConfig?.IsKeycloakLoginActive is true)
        {
            // Login: lives outside the Blazor SSR pipeline so ChallengeAsync can
            // write the 302 without conflicting with Blazor rendering.
            // CSRF note: the .NET OIDC handler generates a correlation cookie + state
            // parameter on ChallengeAsync and validates them on the callback — login
            // CSRF is mitigated at the framework level.
            app.MapGet("/auth/oidc-login", async (HttpContext ctx, string? returnUrl) =>
            {
                // Clear ALL stale OIDC/auth cookies AND session cookie chunks
                // from previous sessions to prevent "Correlation failed" and
                // chunk mismatch on re-login after logout.
                foreach (var cookie in ctx.Request.Cookies.Keys.ToList())
                {
                    if (cookie.StartsWith(".AspNetCore.", StringComparison.Ordinal)
                        && !cookie.StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal))
                    {
                        ctx.Response.Cookies.Delete(cookie);
                    }

                    // Delete stale session cookie chunks (stratum_sessionC1, C2, ...)
                    if (cookie.StartsWith("stratum_session", StringComparison.Ordinal))
                    {
                        ctx.Response.Cookies.Delete(cookie);
                    }
                }

                await ctx.ChallengeAsync(
                    OpenIdConnectDefaults.AuthenticationScheme,
                    new AuthenticationProperties
                    {
                        RedirectUri = ReturnUrlSanitizer.Sanitize(returnUrl),
                    });
            }).AllowAnonymous();

            // Logout: signs out of OIDC + cookie outside Blazor SSR.
            // Order matters: OIDC sign-out reads the id_token from the cookie first,
            // then cookie sign-out clears the session. Both headers ship in one response.
            app.MapGet("/auth/oidc-logout", async (HttpContext ctx) =>
            {
                var kc = ctx.RequestServices.GetRequiredService<IOptions<KeycloakSettings>>().Value;
                await ctx.SignOutAsync(
                    OpenIdConnectDefaults.AuthenticationScheme,
                    new AuthenticationProperties
                    {
                        RedirectUri = ReturnUrlSanitizer.Sanitize(kc.PostLogoutRedirectUri),
                    });
                await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }).RequireAuthorization();
        }

        // Test-only fallback login: cookie-based auth without Keycloak.
        // Only registered in Test environment with UseKeycloak=false.
        if (app.Environment.IsEnvironment("Test") && kcConfig?.IsKeycloakLoginActive is not true)
        {
            app.MapPost("/auth/test-login", async (HttpContext ctx) =>
            {
                var form = await ctx.Request.ReadFormAsync();
                var username = form["username"].ToString();
                var returnUrl = form["returnUrl"].ToString();

                if (string.IsNullOrWhiteSpace(username))
                {
                    return Results.BadRequest("username is required");
                }

                var identityQueries = ctx.RequestServices.GetRequiredService<IIdentityQueries>();
                var user = await identityQueries.GetUserByUsername(username);
                if (user is null || !user.IsActive)
                {
                    return Results.Redirect("/login?error=invalid");
                }

                var permissions = await identityQueries.GetUserPermissions(user.Id);
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new(ClaimTypes.Name, user.Username),
                    new("preferred_username", user.Username),
                    new(ClaimTypes.Email, user.Email),
                    new("name", user.DisplayName),
                };

                foreach (var role in user.Roles)
                {
                    claims.Add(new Claim(ClaimTypes.Role, role));
                }

                foreach (var perm in permissions)
                {
                    claims.Add(new Claim("permission", perm));
                }

                claims.Add(new Claim("company_id", Guid.Empty.ToString("D")));

                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                await ctx.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(identity));

                var safe = ReturnUrlSanitizer.Sanitize(returnUrl);
                return Results.Redirect(safe);
            }).AllowAnonymous();
        }

        // API versioning — all REST endpoints under /api/v1/...
        var versionSet = app.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1, 0))
            .Build();

        var v1 = app.MapGroup("/api/v{version:apiVersion}")
            .WithApiVersionSet(versionSet)
            .MapToApiVersion(new ApiVersion(1, 0));

        // Culture switch endpoint
        v1.MapPost("/culture", (HttpContext ctx) =>
        {
            var culture = ctx.Request.Form["culture"].ToString();
            if (string.IsNullOrEmpty(culture) || !SupportedCultures.IsSupported(culture))
            {
                return Results.BadRequest();
            }

            ctx.Response.Cookies.Append(
                ".AspNetCore.Culture",
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true });

            var returnUrl = ctx.Request.Form["returnUrl"].ToString();
            var safe = !string.IsNullOrEmpty(returnUrl)
                && Uri.TryCreate(returnUrl, UriKind.Relative, out _)
                && returnUrl.StartsWith('/')
                && !returnUrl.StartsWith("//", StringComparison.Ordinal)
                && !returnUrl.Contains('\\')
                    ? returnUrl
                    : "/";
            return Results.Redirect(safe);
        }).AllowAnonymous();

        v1.MapIdentityEndpoints();
        v1.MapJobEndpoints();
        v1.MapNotificationEndpoints();
        v1.MapAuditEndpoints();
        v1.MapTenantAdminEndpoints();
        v1.MapClientExportEndpoints();
        v1.MapDocumentsEndpoints();
        v1.MapDocumentActionsEndpoints();
        v1.MapPipelineEndpoints();
        v1.MapArchiveEndpoints();
        v1.MapTenantSettingsEndpoints();
        v1.MapTvaMappingEndpoints();
        v1.MapReconciliationEndpoints();
        v1.MapAgentManagementEndpoints();
        v1.MapSignatureEndpoints();

        // API agent → plateforme (contrat d'ingestion, F12 §3) : groupe /api/agent/v1 distinct de
        // l'API console OIDC, authentifié par clé API (filtre) et protégé par rate limiting.
        app.MapAgentApi();

        // Endpoint central de la flotte (OPS04) : POST /api/fleet/v1/heartbeat, authentifié par clé
        // d'ingestion (en-tête X-Fleet-Key), actif seulement si le rôle central est activé (sinon 404).
        app.MapFleetApi();

        // Webhooks de signature (SIG07, ADR-0029) : POST /webhooks/signature/{providerType}/{opaqueRef},
        // anonyme (garde = HMAC par tenant), routage par handle opaque + inbox durable avant 2xx.
        app.MapSignatureWebhooks();

        app.MapRazorComponents<App>()
            .AddAdditionalAssemblies(
                typeof(Stratum.Modules.Notification.Web.NotificationEndpointMapping).Assembly,
                typeof(Stratum.Modules.Identity.Web.IdentityEndpointMapping).Assembly,
                typeof(Stratum.Modules.Audit.Web.AuditNavSectionProvider).Assembly,
                typeof(Stratum.Modules.Job.Web.JobNavSectionProvider).Assembly)
            .AddInteractiveServerRenderMode();
    }

    /// <summary>
    /// Valide au démarrage les fournisseurs de signature CONFIGURÉS (SIG03, ADR-0027 §4). Lit
    /// <c>Signature:EnabledProviders</c> (liste de types de plug-ins activés, vide par défaut) et délègue à
    /// <see cref="SignatureProviderStartupValidator"/> : bloque le démarrage UNIQUEMENT pour un fournisseur
    /// configuré mais non câblé ; l'absence de tout fournisseur n'est jamais une erreur (signature
    /// optionnelle — INV-SIGPROV-6). La logique pure est testée par <c>SignatureProviderStartupValidatorTests</c>.
    /// </summary>
    /// <param name="configuration">Configuration de l'application (lit <c>Signature:EnabledProviders</c>).</param>
    /// <param name="services">Fournisseur de services (résout <see cref="ISignatureProviderRegistry"/>).</param>
    internal static void ValidateSignatureProviderConfiguration(
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        IServiceProvider services)
    {
        var enabledProviders = configuration
            .GetSection("Signature:EnabledProviders")
            .Get<string[]>() ?? [];

        var registry = services.GetRequiredService<ISignatureProviderRegistry>();
        SignatureProviderStartupValidator.Validate(enabledProviders, registry);
    }

    /// <summary>
    /// Validation au démarrage du flag de profil de déploiement <c>Keycloak:DedicatedRealmPerTenant</c>
    /// (RDF11, redline ADR fondateurs RL-IDP-8). Délègue la décision pure (fail-closed) à
    /// <see cref="DedicatedRealmStartupValidator"/> : en profil dédié sans API Admin Keycloak
    /// configurée, le provisioning realm-par-tenant est impossible → échec explicite au démarrage.
    /// No-op en profil SaaS partagé (défaut). Le flag est lu via la MÊME clé
    /// (<c>Keycloak:DedicatedRealmPerTenant</c>) et la MÊME notion « API Admin configurée »
    /// (<see cref="KeycloakAdminOptions.IsConfigured"/>) que les sites consommateurs (sélection DI du
    /// provisioner, ciblage de realm), garantissant la cohérence au démarrage. La logique pure est
    /// testée par <c>DedicatedRealmStartupValidatorTests</c>.
    /// </summary>
    /// <param name="configuration">Configuration de l'application (lit la section <c>Keycloak</c>).</param>
    internal static void ValidateDedicatedRealmConfiguration(
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var dedicated = configuration.GetValue<bool>(
            $"{KeycloakAdminOptions.SectionName}:DedicatedRealmPerTenant");
        var adminConfigured =
            configuration.GetSection(KeycloakAdminOptions.SectionName).Get<KeycloakAdminOptions>()?.IsConfigured
            ?? false;

        DedicatedRealmStartupValidator.Validate(dedicated, adminConfigured);
    }

    /// <summary>
    /// Applies any missing migrations to all active tenant databases.
    /// Must run after system migrations so that <c>outbox.tenants</c> is available.
    /// </summary>
    private static async Task MigrateExistingTenantsAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<ITenantProvisioningService>();
        await provisioner.MigrateExistingTenantsAsync(app.Lifetime.ApplicationStopping);
    }

    /// <summary>
    /// Sélectionne l'implémentation d'<see cref="IIdentityProviderAuthenticator"/> à utiliser
    /// selon « Identity:Provider » (décision D10). Par défaut Keycloak. À ce jour il n'existe
    /// qu'UNE entrée (Keycloak) : 0 implémentation OpenIddict. Ajouter une entrée au registre
    /// ci-dessous est NÉCESSAIRE mais NON SUFFISANT pour brancher un autre IdP — le provisioning
    /// realm/utilisateur, le 2FA et la résolution issuer/JWKS sont Keycloak-spécifiques et câblés
    /// hors du sélecteur (voir avenant ADR-0002 du 2026-06-20 / RDF09).
    /// </summary>
    private static IIdentityProviderAuthenticator SelectIdentityProvider(
        string? providerName,
        KeycloakSettings keycloakSettings)
    {
        // Registre des fabriques d'IdP, indexé par nom de fournisseur. Une alternative in-process
        // (ex. OpenIddict) s'ajouterait ici, mais l'entrée seule ne suffit pas : voir le résumé de
        // méthode (provisioning/2FA/JWKS hors sélecteur) et l'avenant ADR-0002 du 2026-06-20.
        var providers = new Dictionary<string, Func<IIdentityProviderAuthenticator>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Keycloak"] = () => new KeycloakIdentityProviderAuthenticator(keycloakSettings),
        };

        // Défaut : Keycloak (aucun provider explicite configuré).
        var selected = string.IsNullOrWhiteSpace(providerName) ? "Keycloak" : providerName;

        if (providers.TryGetValue(selected, out var factory))
        {
            return factory();
        }

        // On bloque plutôt que de démarrer avec une authentification incorrecte.
        throw new InvalidOperationException(
            $"Fournisseur d'identité « {providerName} » inconnu. Implémentations disponibles : "
            + $"{string.Join(", ", providers.Keys)}. Branchez une implémentation "
            + "d'IIdentityProviderAuthenticator pour ce fournisseur (décision D10).");
    }

    /// <summary>
    /// Loads existing tenant-realm mappings from the database into the in-memory
    /// <see cref="IRealmRegistry"/> so that JWTs from previously provisioned realms
    /// are accepted without requiring them in static config.
    /// </summary>
    private static async Task SeedRealmRegistryFromDatabaseAsync(WebApplication app)
    {
        var kc = app.Configuration.GetSection("Keycloak").Get<KeycloakSettings>();

        if (kc is null || !kc.IsConfigured)
        {
            return;
        }

        // RLM04 (ADR-0021 §1/§5) : en profil SaaS PARTAGÉ (défaut), il n'existe qu'UN realm partagé —
        // déjà enregistré au câblage de l'auth via Keycloak:RealmTenantMap (KeycloakIdentityProvider-
        // Authenticator). Les realm_name par-tenant d'outbox.tenants sont alors VESTIGIAUX (placeholders
        // UNIQUE pour des realms qui n'existent plus) : les enregistrer comme émetteurs JWT pointerait
        // vers des autorités inexistantes (échecs JWKS). On ne seede le registre depuis la base que dans
        // le profil DÉDIÉ mono-tenant (Keycloak:DedicatedRealmPerTenant=true), où chaque tenant a son
        // realm réel.
        if (!app.Configuration.GetValue<bool>("Keycloak:DedicatedRealmPerTenant"))
        {
            return;
        }

        var realmRegistry = app.Services.GetRequiredService<IRealmRegistry>();
        var tenantQueries = app.Services.GetRequiredService<ITenantQueries>();

        var baseUrl = kc.Authority[..kc.Authority.LastIndexOf("/realms/", StringComparison.Ordinal)];
        var tenants = await tenantQueries.ListAsync();

        foreach (var tenant in tenants.Where(t => t.IsActive && t.RealmName is not null))
        {
            var authority = $"{baseUrl}/realms/{tenant.RealmName}";
            realmRegistry.RegisterRealm(tenant.RealmName!, tenant.Id, authority);
        }
    }
}
