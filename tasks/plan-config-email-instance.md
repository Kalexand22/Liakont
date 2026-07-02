# Plan — Configuration email d'instance multi-provider chiffrée

> **✅ LIVRÉ (2026-07-01, branche `feat/recette-encheres-config-email-instance`).** Tous les lots ci-dessous
> sont implémentés (avec un écart documenté : orchestration par **service console Host**
> `IInstanceEmailConfigService` plutôt qu'un handler MediatR — l'assembly Host n'étant scanné par aucun
> `AddMediatR` ; précédent `GeneriqueAccountResolver`). `verify-fast` + `run-tests` (7205 tests) verts.
> Voir la **Note d'implémentation** d'ADR-0039. Fusion humaine à venir.


> Spec : **ADR-0039** (`docs/adr/ADR-0039-config-email-instance-multi-provider-chiffree.md`), amende ADR-0018.
> Demande : `tasks/bugs-recette-encheres-b2c.md` (« Config d'envoi d'emails en Supervision »).
>
> Principe : seam vendored `IEmailTransport` **inchangé** ; multi-provider (SmtpBasic/GoogleOAuth2/
> MicrosoftOAuth2) derrière lui, via **MailKit XOAUTH2 natif** (aucun package) ; secrets **chiffrés**
> (`ISecretProtector`, purposes dédiés) ; config en **base système** (module instance-level, PAS Supervision) ;
> console gate **`liakont.instance.settings`** (nouvelle permission d'écriture).

## Lot 1 — Persistance instance (module instance-level, base système)

- [ ] `IInstanceEmailConfigStore` (Contracts du module) : Read → DTO `kind/host/port/tls/username/from` +
  booléens `Has*` + champs **ciphertext opaques** (jamais de clair) ; Write reçoit du ciphertext déjà chiffré.
- [ ] `PostgresInstanceEmailConfigStore` via **`ISystemConnectionFactory`** (précédent `PostgresFleetStore`),
  ligne **singleton** d'instance.
- [ ] Migration `V00N__create_instance_email_config.sql` : colonnes `kind, host, port, use_starttls, username,
  from_address, from_name, enabled, encrypted_smtp_password, encrypted_oauth_client_secret,
  encrypted_oauth_refresh_token, oauth_client_id (CLAIR), oauth_tenant_id (CLAIR), updated_at`. Note « base système ».
- [ ] Home = **FleetSupervision** (déjà base système/écriture/emails) OU module dédié `InstanceSettings`
  (mutualisable avec `Reference` d'ADR-0038). **PAS Supervision** (tenant-scopé, lecture seule).

## Lot 2 — Secrets (Host, chiffrement)

- [ ] `EmailSecretPurposes.cs` (Host) : purposes DP dédiés `.v1` — `SmtpPassword`, `OAuthClientSecret`,
  `OAuthRefreshToken` (miroir `PaAccountSecretPurposes`). `client_id`/`tenant_id` = **non-secrets** (clair).
- [ ] `SaveInstanceEmailConfig` (command + handler MediatR, Host) : **chiffre** via `ISecretProtector`
  (patron `AddPaAccountHandler`) puis persiste le ciphertext. **Lit-puis-conserve** : un champ secret **vide**
  au save **garde** le ciphertext existant (ne jamais écraser par du vide). Le clair ne quitte pas le handler.

## Lot 3 — Transport provider-aware + token OAuth (Host)

- [ ] `IEmailOAuthTokenProvider` (Host) : `Task<string> GetAccessTokenAsync(EmailOAuthCredentials, ct)`.
- [ ] `HttpEmailOAuthTokenProvider` (Host) : grant `refresh_token` via `IHttpClientFactory` (endpoints Google/
  Microsoft selon kind) + `System.Text.Json`. **AUCUN SDK.** Aucun secret en log. **Singleton** (ou
  `IMemoryCache`) pour honorer `expires_in`.
- [ ] `SmtpEmailTransport` provider-aware : charge la config d'instance (déchiffrée en mémoire Host), branche
  l'auth sur le **kind** — `SmtpBasic`: `AuthenticateAsync(user,pwd)` ; `Google/MicrosoftOAuth2`:
  `AuthenticateAsync(new SaslMechanismOAuth2(user, accessToken))`. Host/Port/StartTls depuis la config DB.
  **No-op non-bloquant** si non configuré (ADR-0018 §4).

## Lot 4 — Précédence + DI

- [ ] Précédence : ligne DB **`enabled`** = **autoritaire** (host/port/tls/auth/secrets) ; `SmtpOptions`/
  `appsettings` = **repli bootstrap** uniquement (aucune ligne DB). Tester les deux branches.
- [ ] `AppBootstrap` (:197-198) : enregistrer `IInstanceEmailConfigStore`, `IEmailOAuthTokenProvider`→
  `HttpEmailOAuthTokenProvider` (Singleton), HttpClient nommé pour les endpoints token, garder le `Replace` de
  `IEmailTransport` par le `SmtpEmailTransport` provider-aware.

## Lot 5 — Console (gabarit AdminIntegrations, permission dédiée)

- [ ] **Nouvelle permission** `LiakontPermissions.InstanceSettings = "liakont.instance.settings"` (+ rôle Keycloak).
- [ ] `EmailSettings.razor` (Host), gabarit **`AdminIntegrations.razor`** (StratumTabs/SectionCard/FormField/
  PermissionGate/StratumButton) : select de `kind` révélant les champs OAuth, secrets `type=password` masqués
  (placeholder `•••` si `Has*`), boutons **Enregistrer** + **« Envoyer un email de test »**. Zéro logique métier
  (délègue au handler). `@attribute [Authorize(Policy = LiakontPermissions.InstanceSettings)]`.
- [ ] Nav : entrée « Configuration email » sous l'aire opérateur d'instance. Auditer la nav (JAMAIS d'item « démo »).

## Lot 6 — ADR-0018 + tests

- [ ] Amender/superséder `ADR-0018` (config `appsettings`→DB chiffrée, multi-provider XOAUTH2, décision de NE PAS
  tirer MSAL/Graph/Google SDK en V1).
- [ ] Tests : bUnit page (kind révèle OAuth, secrets masqués, gate, délégation MediatR, bouton test) ; transport
  (basic vs XOAUTH2, no-op si non configuré) ; `HttpEmailOAuthTokenProvider` (bonne requête par endpoint, parse,
  échec, aucun secret log) ; purposes/handler (isolation crypto, ciphertext-only, vide ne remplace pas, DTO `Has*`) ;
  intégration Testcontainers (round-trip base système, colonnes = ciphertext, upsert singleton) ; boundary
  (MailKit/`ISecretProtector` ne fuient pas dans un module métier ; socle Notification non modifié).

## Points ouverts (ADR-0039 §Points NON TRANCHÉS)

- **D1** home du store (FleetSupervision vs module dédié `InstanceSettings`).
- **D2** O365 : SMTP XOAUTH2 (V1) vs Graph sendMail (fast-follow).
- **D3** obtention initiale du refresh_token (hors code, déploiement).
- **D4** permission (nouvelle `liakont.instance.settings`).
