# Raccordement Chorus Pro (qualification) — note d'exploitation

Procédure de raccordement d'un tenant à **Chorus Pro via PISTE** en **qualification** (sandbox),
et report des valeurs obtenues dans le paramétrage Liakont. Le plug-in ne fait que **transporter**
un Factur-X déjà scellé (aucune logique fiscale). Source : [`docs/conception/F18-Plugin-ChorusPro.md`](../docs/conception/F18-Plugin-ChorusPro.md)
et `tasks/plan-chorus-pro.md` §3.

> ⚠️ Aucun secret ne se versionne ici (CLAUDE.md n°10). Les `client_id` / `client_secret` OAuth2
> PISTE et le mot de passe du compte technique Chorus Pro se saisissent **uniquement** depuis la
> console (Paramétrage › Plateforme Agréée), chiffrés en base par tenant. Cette note ne décrit que
> la procédure et le **mapping** des valeurs **non sensibles**.

## 1. Prérequis externes à obtenir (délai d'obtention — à lancer en amont)

1. Compte portail **PISTE** (`piste.gouv.fr`) + **application SANDBOX** déclarée
   → fournit `client_id` / `client_secret` OAuth2 de **qualification** (= secrets, saisis console).
2. **Raccordement API** déclaré côté portail Chorus Pro qualif (rattacher l'app SANDBOX).
3. **Compte technique de qualification** Chorus Pro (login / mot de passe), rattaché à un
   **SIRET de test** → en-tête `cpro-account: base64(login:motDePasse)` (F18 §2.2). Le login est
   non sensible (va dans `accountIdentifiers`) ; le mot de passe est un **secret** (saisi console).
4. Souscription aux **API Chorus Pro** (CGU cochées sur l'application PISTE).

## 2. Endpoints de qualification (sandbox PISTE)

À **verrouiller sur le Swagger PISTE courant** au raccordement (F18 §2.1/§3.3 — jamais figés dans le
code, fournis par compte) :

| Rôle | Valeur qualif (sandbox) |
|---|---|
| Endpoint jeton OAuth2 (`client_credentials`, `scope=openid`) | `https://sandbox-oauth.piste.gouv.fr/api/oauth/token` |
| Base API Chorus Pro (`cpro`) | `https://sandbox-api.piste.gouv.fr/cpro/` |

En production : mêmes hôtes **sans** le préfixe `sandbox-` (`oauth.piste.gouv.fr`,
`api.piste.gouv.fr/cpro/`). Toujours `*.piste.gouv.fr` — **jamais** `aife.economie.gouv.fr`
(décommissionné le 30/09/2023, F18 §7).

## 3. Report des valeurs dans le paramétrage Liakont

Double authentification (F18 §2) : jeton OAuth2 du compte **PISTE** + en-tête `cpro-account` du
compte **technique Chorus Pro** (distinct). Les valeurs se répartissent ainsi :

### a) Champ « Identifiant de compte » — `accountIdentifiers` (JSON, NON sensible, console ou seed)

Saisi dans la console (CP06) ou versionné en seed `pa-accounts.json`. Clés attendues
(`ChorusProAccountResolver`) :

| Clé | Contenu | Source |
|---|---|---|
| `accountId` | Identifiant de compte (audit/lectures) | interne |
| `technicalLogin` | Login du compte technique Chorus Pro (`cpro-account`) | prérequis §1.3 |
| `connectionEmail` | E-mail de connexion du compte technique (résolution `idUtilisateurCourant`, F18 §3.2) | prérequis §1.3 |
| `baseUrl` | Base API `cpro` (absolue, *.piste.gouv.fr) | §2 |
| `tokenEndpoint` | Endpoint jeton OAuth2 PISTE (absolu) | §2 |

Exemple **fictif** au format attendu :
[`config/exemples/tenant-seed/pa-accounts.json`](../config/exemples/tenant-seed/pa-accounts.json)
(entrée `pluginType: "ChorusPro"`, `environment: "Staging"`, **aucun secret**).

### b) Secrets — saisis depuis la console UNIQUEMENT (jamais en seed, chiffrés en base)

| Secret | Source |
|---|---|
| `client_id` OAuth2 PISTE | prérequis §1.1 |
| `client_secret` OAuth2 PISTE | prérequis §1.1 |
| Mot de passe du compte technique Chorus Pro | prérequis §1.3 |

L'import de seed **n'écrit jamais** de secret (CLAUDE.md n°10) : si un compte est créé par seed, ses
secrets restent vides → l'envoi est **bloqué** (message opérateur FR) tant qu'ils ne sont pas saisis
et activés en console.

## 4. Vérification du raccordement (recette qualif)

La recette réelle est portée par la gate humaine **GATE_CHORUS_PRO** (plan §6) : dépôt
`deposerFluxFacture` → `PaSendState.Sending` (jamais `Issued` au dépôt) → `consulterCR` →
`Issued` **uniquement** sur `Intégré`, sans re-dépôt sur timeout, aucun credential journalisé.

## 5. Déploiement client réel

Le seed concret d'un client (table TVA validée, SIREN, comptes PA réels) vit dans
`deployments/<client>/`, **jamais** dans `config/exemples/` ni dans `src/` (CLAUDE.md n°7/15 —
voir [`README.md`](README.md)).
