# SUP03 — Notifications email d'alerte (+ transport SMTP réel)

Segment console-web · sous-branche `feat/console-web-SUP03` · slot-3 · session `orch-20260608-191844-Liakont-s3`

## Contexte
- Le socle Stratum fournit le PIPELINE de notification (templates, queue, retry) mais son seul
  transport est `StubEmailTransport` (log only). Aucun package SMTP dans `Directory.Packages.props`.
- Hooks lifecycle = `AlertEvaluationService.ApplyAsync` (SUP01a) : raise (Insert) + resolve (Resolve)
  → anti-spam naturel (un email au déclenchement, un à la résolution, jamais de répétition).
- Recipients déjà persistés : `TenantProfileDto.ContactEmailAlerte` + `AlertThresholdsDto.AlertTenantContact`
  (TenantSettings, tenant-scopé). Opérateur d'instance = config instance (appsettings).
- Décision archi : composer le sujet/corps FR dans Supervision et enfiler un `EmailSendJobPayload`
  via `IJobQueue` (réutilise queue+retry+transport du module Notification) — pas de template DB
  (la résolution de template n'a pas de fallback tenant→système). Transport SMTP réel = Host (composition
  root), sur l'abstraction `IEmailTransport` vendored (socle NON modifié → pas de provenance à toucher).

## Plan (checkable)
- [ ] ADR-0018 : dépendance MailKit (+ MimeKit) — justification, alternatives rejetées.
- [ ] `Directory.Packages.props` : `MailKit` + `MimeKit` 4.17.0.
- [ ] `SmtpOptions` (Host, section `Smtp`) : Host/Port/User/Password/From/UseStartTls/Enabled.
- [ ] `SmtpEmailTransport` (Host, internal) : MailKit ; désactivé/non configuré = log + no-op (pas de throw,
      pas de retry infini) ; activé + échec = throw (le job retente, non bloquant).
- [ ] `appsettings.json` : sections `Smtp` (placeholder vide, mot de passe jamais en clair) + `Supervision:Notifications`.
- [ ] AppBootstrap : `Replace` du stub par `SmtpEmailTransport` + bind `SmtpOptions` + `SupervisionNotificationOptions`.
- [ ] `IAlertNotifier` (Application) : `NotifyRaisedAsync` / `NotifyResolvedAsync` — contrat « ne lève JAMAIS ».
- [ ] `NullAlertNotifier` (Application) : no-op (défaut des ctors existants → tests SUP01a/b intacts).
- [ ] `SupervisionNotificationOptions` (Application) : OperatorEmail, SendResolutionEmails, DailyDigestEnabled.
- [ ] `AlertEvaluationService` : ctor 4-args (+ IAlertNotifier), hooks raise/resolve ; 2/3-args délèguent au Null.
- [ ] `AlertEmailNotifier` (Infrastructure) : résolution destinataires (opérateur=tout ; contact tenant=critique+opt-in),
      corps FR actionnable, enqueue `EmailSendJobPayload` (companyId tenant), try/catch→log (jamais throw).
- [ ] Digest quotidien optionnel (default off) : `IAlertDigestSender` + `SupervisionDigest{Trigger,FanOutHandler,TenantJob}`,
      résumé des alertes actives à l'opérateur, gated `DailyDigestEnabled`.
- [ ] DI Supervision : `IAlertNotifier`→`AlertEmailNotifier`, `IAlertDigestSender`→`AlertEmailNotifier`, factory moteur MAJ.
- [ ] csproj Supervision.Infrastructure : ref `Notification.Contracts` (Contracts-only, OK module-rules) ; pas de secret.
- [ ] Tests unitaires : destinataires par gravité/opt-in, corps FR, anti-spam (transitions only), non-bloquant ;
      hook moteur ; construction message SMTP ; digest.
- [ ] verify-fast (plateforme .NET10 + agent net48) + run-tests + codex-review → clean.

## Review
- ✅ ADR-0018 + MailKit/MimeKit 4.17.0 (CPM) ; SmtpOptions + SmtpEmailTransport (Host, internal) ;
  `Replace` du stub au composition root ; appsettings placeholders (mot de passe vide).
- ✅ IAlertNotifier (+ NullAlertNotifier défaut → tests SUP01a/b intacts) ; hooks raise/resolve dans
  AlertEvaluationService (transitions only = anti-spam) ; AlertEmailNotifier (opérateur=tout, contact
  tenant=critique+opt-in), corps FR actionnable, enqueue EmailSendJobPayload, fire-and-log.
- ✅ Digest quotidien optionnel (default off) : IAlertDigestSender + SupervisionDigest{Trigger,FanOutHandler,TenantJob}.
- ✅ Supervision ne porte aucun secret (mot de passe SMTP = Host) ; refs Contracts-only (Notification/TenantSettings).
- ✅ verify-fast PASS (plateforme .NET10 + agent net48). run-tests PASS (4436 tests, 0 échec).
- Tests : 16 nouveaux tests unitaires (notifier 12 + moteur 4 + SMTP 4). codex-review : à lancer.
