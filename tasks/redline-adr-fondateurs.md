# Redline — ADR fondateurs (ADR-0001 pivot, ADR-0002 IdP, ADR-0003 stack agent)

> Revue prospective des 3 ADR fondateurs **et de tout ce qui en a découlé**, demandée par Karl
> le 2026-06-19 (« refaire une passe, comme l'IA devient meilleure, sur ce qu'on aurait loupé
> qui nous impacterait par la suite »). Méthode : 5 axes de critique (un par ADR fondateur +
> un axe transverse), 34 findings produits, chacun passé en **vérification contradictoire
> indépendante** contre le code réel (réel ? déjà traité par un ADR ultérieur ? sévérité ?) →
> 32 retenus, 2 réfutés. Ce document est la **source** des items du lot `RDF` (segment
> `adr-redline-fondateurs`).

## 1. Synthèse exécutive

Les trois ADR fondateurs sont **structurellement sains** : aucune décision n'est à reconsidérer,
et les principes durs (`decimal`, lecture seule, append-only, tenant-scope, secrets chiffrés)
tiennent dans le code. Le problème dominant n'est pas une mauvaise décision mais un **décalage
ADR ↔ réalité** sur les promesses différées : plusieurs « réversibilités » fondatrices (option D
NuGet, repo agent séparé, OpenIddict, contrat N/N-1) sont **décrites comme acquises ou « à terme »
mais jamais exercées**, et leur dette croît silencieusement.

Quatre risques majeurs, par gravité :

1. **TLS 1.2 jamais forcé sur le chemin de données réel de l'agent** (P1) — seul vrai défaut
   runtime : time-bomb net48 + faux-vert d'installation (`test-api` force TLS dans un *autre*
   processus que le service qui pousse).
2. **Realm de production (appliance) non durci** (P1/P2) — 2FA non enrôlé et `company_id` non
   immuable sur le realm que le client exécute, parce que les tests de sécurité ne couvrent que
   les realms dev/E2E (faux-vert de test).
3. **Réversibilités fictives en chaîne** (P2) — option D NuGet, repo agent séparé, OpenIddict,
   capacités d'extraction : seams promis jamais éprouvés par une 2ᵉ implémentation.
4. **Time-bombs licence/CVE non gouvernées** (P2) — SQLite 3.46.1 sous CVE + EOL,
   FluentAssertions 8.x devenu commercial non tracé, QuestPDF à seuil de CA sur le chemin
   d'émission fiscale.

**Verdict global : ADR à AMENDER, pas à reconsidérer.** Une action P1 code (TLS) + deux P1
config/test (realm prod) avant toute prod ; le reste est de la mise à jour de statut d'ADR et de
gouvernance de dette.

## 2. Redline par ADR

### ADR-0001 — Pivot plateforme + agent

| Sév. | Réf | Risque réel | Recommandation |
|---|---|---|---|
| **P1** | RL-AGT-1 | `AgentRunComposition.CreateHttpClient` (`agent/.../Hosting/AgentRunComposition.cs:91-97`) ne pose **jamais** `ServicePointManager.SecurityProtocol`. TLS forcé seulement dans la sonde diag et l'updater — **3 processus distincts**. Sur vieux Windows sans `SchUseStrongCrypto`, le service réel négocie TLS 1.0/1.1 pendant que `test-api` valide en vert → critère AGT02 = faux-vert. | Poser `SecurityProtocol \|= Tls12 \| Tls13` au démarrage de **chaque** `Main` (`Liakont.Agent` ET `.Cli`), + test prouvant le client de run, + durcir `app.config`. |
| **P2** | RL-SOCLE-1 | Re-convergence NuGet (option D) fictive : 60/121 projets référencent du `Stratum.*` ; ~30 sous-sections de dérive dont des modifs **structurellement Liakont** (non reversables). `socle-provenance-check.ps1` mesure la dérive **post-régénération**, jamais vs le commit source `1454c7f`. | Requalifier « option D visée à terme » en « non planifiée / à réévaluer » OU adosser critère + budget. Politique de **backport des correctifs de sécurité Stratum amont**. Mesure de dérive cumulée vs `1454c7f`. |
| **P2** | RL-AGT-2 | Capacités d'extraction (ADR-0004 D2) jamais remontées : `ExtractorCapabilities` vit dans `Liakont.Agent.Core`, **pas dans Contracts** ; 0 consommateur plateforme. Seam à un seul bout. *(Déjà tracé/bloqué : API01d ; impact présent nul.)* | À régler **avant la 2ᵉ/3ᵉ intégration source** sous peine de retomber dans l'overfit qu'ADR-0004 voulait éviter. (Hors lot RDF : suivi via API01d.) |
| **P2** | RL-AGT-6 | Lecture seule = garde de préfixe SELECT. Verrous explicites + transactions d'écriture déjà **interdits et prouvés** (`TransactionsBegun==0`). Résidu : verrous **partagés implicites** du moteur (READ COMMITTED) pendant un SELECT. | Documenter que la suppression des verrous de lecture relève du paramétrage tenant (`ApplicationIntent=ReadOnly`/snapshot). Exemple par moteur dans `config/exemples/`. |
| **P3** | RL-SOCLE-3 | Préfixe base `stratum_` jamais surchargé → bases prod nommées `stratum_<id>` (marque socle visible côté opérateur SQL). | **Décider et tracer** : `stratum_` assumé vs `liakont_`, **à arbitrer avec RLF01** (seed dev déjà aligné). |
| **P3** | RL-SOCLE-4 | Frontière de nommage non gardée par un test de direction : la provenance juge le **contenu** (hash), pas la **direction**. Aucun NetArchTest n'interdit `Stratum.* → Liakont.*` → un `.csproj` socle pourrait référencer du Liakont et compiler vert → non reversable. | NetArchTest `Stratum.*` → `NotHaveDependencyOn("Liakont")`. |
| **P3** | RL-SOCLE-2 | Migration tenant sérielle au boot, plafond non documenté. Boot O(N) ; échec DbUp avorte tout — mais c'est un **fail-closed délibéré et documenté**. | Documenter un **ordre de grandeur tenants/instance par topologie** + seuil de sharding. (Ne **pas** passer en migration paresseuse : contredit le fail-closed.) |
| ✅ | RL-SOCLE-6 | Reliquats on-premise (net48/WPF/SQLite) côté plateforme : `src/**` propre (0 hors 2 commentaires historiques). | **Aucune action.** Angle vérifié négatif. |

### ADR-0002 — Empreinte IdP (Keycloak vs OpenIddict)

| Sév. | Réf | Risque réel | Recommandation |
|---|---|---|---|
| **P1** | RL-IDP-3 | Realm prod (appliance) sans 2FA forcé : `realm-liakont.json` (importé en prod) a **0 `CONFIGURE_TOTP`, 0 `requiredActions`, 0 `otpPolicy`**. Le durcissement RLF05 a touché realm-export + E2E, **jamais l'appliance**. `SharedRealmConfigTests.RealmFiles()` = dev+E2E seulement → INV-0021-7 prouvé sur un **faux artefact**. | Aligner `realm-liakont.json` sur `realm-export.json` + **étendre `RealmFiles()` à l'appliance**. |
| **P2** | RL-IDP-4 | Realm prod sans User Profile read-only sur `company_id` : aucun composant `declarative-user-profile` → INV-0021-3 non garanti. *(Nuance P2 : sous KC 26, attribut non déclaré + `unmanagedAttributePolicy=DISABLED` rejette l'écriture → écart de conformité + trou de test, pas une fuite active.)* | Porter `declarative-user-profile` (`company_id edit:[admin]`) + politique explicite + étendre tests/E2E au realm prod. |
| **P2** | RL-IDP-5 | Plafond mémoire Keycloak appliance jamais posé : aucun `mem_limit`/`MaxRAMPercentage` → la JVM happe ~1,83 GiB sur gros hôte. La « décision actée » (ADR-0020, ≈1 GiB) est **lettre morte** → OOM/thrash sur VPS, non corrigeable à chaud. | `mem_limit: 1g` + `-XX:MaxRAMPercentage` + **lint CI** de présence du plafond. |
| **P2** | RL-IDP-6 | Keycloak épinglé sur tag flottant `:26.0` **EOL depuis janv. 2025** (~17 mois sans patch). `update-instance.ps1 --pull` ne rafraîchit jamais l'image Keycloak. Retourne l'argument « battle-tested security » d'ADR-0013. | Politique de version + outiller le bump dans OPS02 + pinner un tag de patch précis. |
| **P2** | RL-IDP-1 | Seam IdP D10 jamais exercé par 2 implémentations : registre = **Keycloak seul**, **0 impl OpenIddict**. L'affirmation « seul le sélecteur change » est **fausse** (provisioner/2FA/résolution par issuer câblés indépendamment). Le cas qui *motive* le spike (VPS 15-40 €) n'a pas de solution livrée. | MVP OpenIddict pour **prouver** la réversibilité, OU marquer ADR-0002/0020 « OpenIddict NON IMPLÉMENTÉ » + retirer « seul le sélecteur change ». |
| **P2** | RL-IDP-7 | ADR-0017 : claims OIDC comblés tardivement (latent car E2E en super-admin). **Comblé par IDN01**, trou de test fermé, gate validée (PR #38). Dette résiduelle : fenêtre de révocation (cookie sliding 8h) sur `liakont.actions`/`settings`, acceptée **seulement en commentaire de code**. | Atténuer la fenêtre de révocation + garde CI ≥1 E2E rôle non super-admin par permission sensible + marquer ADR-0013 socle partiellement superseded sur « RBAC unchanged ». |
| **P2** | RL-IDP-2 | Pivot realm→claim non réconcilié : **ADR-0020** (« un realm par tenant ») directement contredit par ADR-0021 (realm unique, isolation par **claim** + cross-check, assumée). ADR-0002 secondaire. | « Partiellement superseded par ADR-0021 » en tête d'**ADR-0020** + note ADR-0002 ; renvoi réciproque à créer. |
| **P2** | RL-IDP-8 | Profil DÉDIÉ mono-tenant hors périmètre des invariants : flag `DedicatedRealmPerTenant` lu en dur en 2 endroits sans validation ; en dédié `CreateRealmAsync` ne pose aucun User Profile read-only. INV-0021-* prouvés **uniquement** pour le SaaS partagé. | Documenter ce que le dédié garantit/perd, **valider le flag au démarrage**, marquer le dédié hors périmètre INV-0021-* tant qu'il n'a pas ses preuves. |

### ADR-0003 — Stack & paquets agent

| Sév. | Réf | Risque réel | Recommandation |
|---|---|---|---|
| **P2** | RL-PKG-1 | `System.Data.SQLite.Core 1.0.119` = SQLite 3.46.1 sous CVE-2025-6965 (fix 3.50.2) et CVE-2025-29088 + branche 1.x EOL. Atténuation : buffer local, aucun input réseau hostile. Mais un scanner de vulns côté client (DSI) lèvera ces CVE → peut **bloquer un déploiement**. | Avenant ADR-0003 : politique de rafraîchissement, cible interop ≥ 3.50.2 (⚠️ 2.x ne bundle plus le moteur → packager `SQLite.Interop.dll` par bitness) + currency CI. |
| **P2** | RL-SER-1 | Re-hash plateforme via STJ jamais testé sur le chemin HTTP réel : le hash est recalculé sur un DTO issu de la **désérialisation STJ**, pas du lecteur canonique. L'intégrité repose sur `STJ.Deserialize(canonique) → CanonicalJson == canonique` octet-pour-octet — **jamais testé**. Tient aujourd'hui, mais une montée .NET / nouveau champ string casserait WORM + anti-doublon silencieusement. | Test : chaque golden canonique → désérialise STJ → re-sérialise CanonicalJson → hash figé. |
| **P2** | RL-AGT-4 | Compatibilité contrat N/N-1 non exerçable : négociation correcte pour V1 (`Previous => null`, 426 testé), mais un seul `MapGroup(/api/agent/v1)`, **pas d'endpoint/fixture/test v2**. Pas un faux-vert, mais une dette de seam — risque au premier changement cassant, sous échéance légale. | Test de cohabitation N/N-1 + golden v2 **avant** la première rupture. |
| **P3** | RL-PKG-2 | `Newtonsoft.Json 13.0.3` figé, **hors chemin d'intégrité** (le hash utilise un writer manuel, Newtonsoft ne sert qu'aux enveloppes non hashées). Libellé ADR « pivot » **trompeur** (c'est du transport). | Bump 13.0.4 + corriger le libellé (transport). |
| **P3** | RL-UPD-1 | Auto-update solide mais Authenticode « fast-follow » + pas de taille de clé min : chaîne conforme (signé RSA/SHA-256 fail-closed, anti-downgrade, rollback). 2 réserves : `RsaManifestSignatureVerifier` ne contrôle aucune taille de clé (1024 bits passerait) ; binaires non signés OS (frottement SmartScreen/EDR). | Imposer RSA ≥ 2048/3072 ; sortir Authenticode du « fast-follow » avant déploiement large. |
| **P3** | RL-AGT-5 | Justification net48 « 32-bit ⇒ net48 » mal cadrée : le 32-bit justifie le build **x86/x64** (bitness process), pas net48. La vraie raison = **compat OS hôte legacy** + zéro runtime à déployer. | Séparer les justifications dans ADR-0001 + documenter la condition de sortie. |
| **P3** | RL-SER-2 | Contrat fil v1 sans négociation, dépend de l'insensibilité de casse STJ : l'agent envoie du PascalCase, lié uniquement par `PropertyNameCaseInsensitive=true` (non documenté). Un durcissement camelCase casserait la liaison **silencieusement**. | Documenter que le fil v1 **EST** le canonique PascalCase + contrôle négatif. |
| **P3** | RL-PKG-3 | `StyleCop.Analyzers 1.2.0-beta.556` (beta figée) : impact runtime **nul** (`PrivateAssets=all`). 1.2.0 stable n'a jamais existé → défendable. | Rien d'urgent. (Hors lot RDF.) |

## 3. Risques transverses / prospectifs

**Incohérences inter-ADR à marquer Superseded/Amended :**
- **ADR-0020** (prioritaire) + **ADR-0002** : « partiellement superseded par ADR-0021 » (frontière crypto par-realm → claim). Renvoi réciproque à créer.
- **ADR-0013 socle** : marquer (côté docs Liakont, **sans modifier le socle**) partiellement superseded sur « RBAC unchanged/Neutral » (permissions recréées sous OIDC).
- **ADR-0005** : re-statuer « Reporté/Conditionnel » + date butoir — le déclencheur (lots AGT) est **entièrement écoulé** sans bascule ; agent **et** plateforme consomment encore `Liakont.Agent.Contracts` par ProjectReference.
- **ADR-0001** : amender pour `clients/OnSiteSignature` (ADR-0030) — « l'agent n'est plus le seul composant client » + séparer la justification net48 / x86.
- **ADR-0011 socle** : ne pas modifier (règle repo) ; nommer la collision 0011 cross-dossier dans `socle/README.md`.

**Time-bombs licence / décision opérateur :**
- **ADR-0030 — 3ᵉ binaire net48 client (Wacom)** hors gouvernance d'auto-update ADR-0013, SDK natif CVE-bearing. Trancher si le client hérite du modèle ADR-0013 (recommandé) ou patch manuel.
- **QuestPDF Community à seuil de CA** sur le scellement PDF/A-3, `LicenseType.Community` codé en dur, ambiguïté du licensee en marque grise. Clarifier par topologie + check au déploiement. Plan B = ZUGFeRD**.PDF**-csharp.
- **FluentAssertions 8.x = commercial** (Xceed, depuis janv. 2025), épinglé `8.2.0` dans 3 catalogues, exempté par ADR-0003 sur une prémisse devenue fausse. >250 fichiers → coût de downgrade croissant. Trancher : 7.x / licence / migration AwesomeAssertions.
- **Réversibilité self-hosted (rassurant)** : la procédure dump/restore/DNS **existe et est testée en round-trip Docker** (`migrate-instance.ps1` + test). Seul résidu : le seam IdP à une implémentation.

## 4. Ce qui va bien (vérifié, rassurant)

- **Lecture seule de la base source prouvée par test**, pas seulement déclarée (`RecordingConnection.BeginTransaction` lève, `TransactionsBegun==0` asserté, `CommandTimeout` 30 s).
- **Le faux risque « 2 sérialiseurs Newtonsoft/STJ sur le hash » n'existe pas** : l'empreinte passe par un **unique writer manuel** (`CanonicalJson`/`PayloadHasher`), aucun Newtonsoft dans `Agent.Contracts`, payload injecté octet-par-octet. Seule la jambe de désérialisation STJ reste à garder (RL-SER-1).
- **Chaîne d'auto-update de l'agent robuste** : manifeste signé fail-closed sans clé, anti-downgrade strict, rollback réel.
- **Périmètre plateforme propre du legacy on-premise** (`src/**` sans net48/WPF/SQLite).
- **Boot fail-closed sur migration assumé et encadré** (jamais servir une base à demi-migrée).

**2 findings réfutés (déjà livrés) :**
- **Isolation realm unique** — déjà mergée sur main (RLM01-04 + GATE_REALM_UNIQUE, recette 2026-06-15) : `TenantCompanyCrossCheckMiddleware` global fail-closed, `company_id` NOT NULL + UNIQUE (V017), mapper hardcodé retiré.
- **Risque calendaire « briques d'émission non codées »** — réfuté : FacturX (CII + PDF/A-3 + tests veraPDF/Mustang), le PA générique Factur-X (indépendant de la sandbox Super PDP) et le module Signature sont **committés sur main**. Seul résidu : ouverture sandbox Super PDP (déjà non-bloquante, blueprint §12).

## 5. Traçabilité → lot d'orchestration

Ce redline est la source du lot **`RDF`** (segment `adr-redline-fondateurs`, gate humaine `GATE_REDLINE_FONDATEURS`).
La correspondance finding → item est dans `orchestration/items/RDF.yaml` (champ `source:` de chaque
item). Les **décisions opérateur attendues** (DEC-1 à DEC-6) y sont inscrites en tête.
