# Plan — Câbler SuperPDP au produit (option 1 : OAuth2 générique + mode d'auth par capacité)

Décidé par Karl 2026-06-17 : option 1, prioritaire (item après RB9). Plan issu d'une exploration
read-only. Branche : feat/emitter-filled-by-platform.

## Découverte clé
Le descripteur `PaAccountDescriptor` construit au SEND (`SendTenantJob.cs:122`) ne porte que
`PluginType + tenantId` (pas de `Settings`). Donc le resolver SuperPDP **ne peut pas** lire les
secrets dans `account.Settings` (contrairement à Yousign). Il doit **lire la table
`tenantsettings.pa_accounts`** par (companyId, plugin_type, actif) et déchiffrer via `ISecretProtector`.
→ nouvelle abstraction interne TenantSettings, calquée sur `ISignatureAccountStore` (option B1).

Modèle actuel : `pa_accounts` n'a qu'UN secret (`encrypted_api_key`). SuperPDP = OAuth2 → 2 secrets
(`client_id` + `client_secret`) + accountId (non secret) + environnement.

## Décisions retenues (défauts recommandés, défendables en build)
- **D1 = B1** : nouvelle abstraction TenantSettings `IPaAccountSecretStore.GetActiveAsync(companyId, pluginType)`
  → (environment, accountIdentifiers, encryptedClientId, encryptedClientSecret). PAS peupler Settings
  (B2 forcerait Pipeline.Infrastructure à voir ISecretProtector → frontière P1).
- **D2** : `PaAuthMode` (enum `ApiKey | OAuth2ClientCredentials`) porté par la fabrique + exposé par le
  registre par TYPE (sans instancier de compte). Léger, suffit à piloter le form.
- **D3** : `PaEnvironment.Staging → SuperPdpEnvironment.Sandbox` ; `Production → Production` (mais BaseUrl
  lève en Production, F14 §12 O1). La console laisse choisir ; le blocage runtime joue (ou désactiver
  Production pour un type OAuth2 — à voir au form).
- **D4** : pour un type OAuth2, « Identifiants de compte » (accountId) devient REQUIS (piloté par AuthMode,
  jamais par le nom du plug-in).
- **D5** : localiser/créer le test bUnit du form `ComptesPaView` (règle 19 = P1).

## Plan ordonné

### PARTIE A — générique (aucun mot « SuperPdp »)
1. `PaAuthMode` enum + `AuthMode` sur `PaCapabilities` (défaut `ApiKey`, rétrocompatible) +
   `PaCapabilitiesSummaryDto`. (`src/Modules/Transmission/Contracts/`, `src/Modules/TenantSettings/Contracts/DTOs/`)
2. Exposer `AuthMode` par TYPE : `IPaClientFactory.AuthMode` + `IPaClientRegistry.DescribeAuthModes()`
   (`Transmission.Contracts` + `Transmission.Infrastructure/PaClientRegistry.cs`) ; impl sur les 4 fabriques
   (SuperPdp, B2Brouter, Generique, Fake) + stubs de test.
3. Migration `V011__add_pa_oauth_credentials.sql` : `ADD COLUMN IF NOT EXISTS encrypted_client_id text;`
   + `encrypted_client_secret text;` (nullable, idempotent — patron V008).
4. Modèle/commandes/UoW : `PaAccount` (+ EncryptedClientId/Secret), `AddPaAccountCommand`/`Update...`
   (+ ClientId/ClientSecret clair), handlers (Protect les 2), `PostgresTenantSettingsUnitOfWork`
   (INSERT/UPDATE/SELECT). Lecture : `PaAccountDto` n'expose que `HasClientId`/`HasClientSecret`
   (INV-TENANTSETTINGS-003 ; jamais le secret, jamais loggé).
5. Form générique multi-secrets piloté par AuthMode : `ComptesPaView.razor` (ApiKey vs ClientId+ClientSecret
   masqués `type=password`), `PaAccountFormModel`, `PaAccountConsoleService`/`PaAccountConsoleModel`
   (map type→AuthMode depuis le registre). Re-rendu sur OnPluginTypeChange.

### PARTIE B — spécifique SuperPDP (seul code qui nomme SuperPDP)
6. `SuperPdpCapabilities.AuthMode = OAuth2ClientCredentials` (+ `SuperPdpClientFactory.AuthMode`).
7. `src/Host/Liakont.Host/PaDelivery/SuperPdpAccountResolver.cs` (modèle GeneriqueAccountResolver +
   YousignAccountResolver) : lit via `IPaAccountSecretStore` (B1), déchiffre, mappe l'env, construit
   `SuperPdpAccountConfig`. Bloque si secret absent.
8. Câblage composition root : `Liakont.Host.csproj` ref `Liakont.PaClients.SuperPdp` ;
   `AddSuperPdpPaClient()` + `TryAddSingleton<ISuperPdpAccountResolver, SuperPdpAccountResolver>()`
   (resolver AVANT la fabrique) — à côté du bloc Yousign (`AppBootstrap.cs:350-351`). Conditionner à la prod.
9. Tests : étendre SuperPdpRegistrationTests, PaAccountConsoleServiceTests, PaAccountIntegrationTests,
   PaClientBootstrapTests, PaCapabilitiesTests ; nouveaux SuperPdpAccountResolverTests + bUnit ComptesPaView
   (OAuth vs ApiKey) + intégration du secret store.

## Précédent Yousign (référence)
`AppBootstrap.cs:350-351` : `TryAddSingleton<IYousignAccountResolver, YousignAccountResolver>()` +
`AddYousignSignatureProvider()`. Resolver `src/Host/Liakont.Host/Signature/YousignAccountResolver.cs`
(internal sealed, ISecretProtector). Store chiffré : `ISignatureAccountStore` +
`PostgresSignatureAccountStore`. SuperPDP mirrore ce patron, MAIS lit pa_accounts (pas Settings).

## Chiffrement existant (à dupliquer pour OAuth)
`AddPaAccountHandler.cs:37-39` → `_secretProtector.Protect(request.ApiKey)`.
`DataProtectionSecretProtector` purpose `Liakont.TenantSettings.PaAccount.ApiKey.v1`.
Migrations : `src/Modules/TenantSettings/Infrastructure/Migrations/` (DbUp, Vxxx, prochaine = V011).
