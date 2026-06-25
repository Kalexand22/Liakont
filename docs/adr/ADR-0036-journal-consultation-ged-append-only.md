# ADR-0036 — Journal de consultation GED append-only (`ged_index.consultation_log`, base tenant, WORM) : best-effort par défaut, fail-closed si finalité probante

- **Statut** : Proposé (2026-06-25).
- **Date** : 2026-06-25
- **Nature** : cet ADR **précède** le chantier d'implémentation (module `Liakont.Modules.Ged` non
  démarré, **aucun code**). Les sections **Décision** et **Invariants** sont **normatives** : elles
  décrivent la **cible**, pas l'état du code. Aucun invariant n'est garanti tant qu'il n'est pas livré
  **et** prouvé par test. Cet ADR **dérive de** la conception `docs/conception/F19-GED-Dynamique-Coffre-Fort.md`
  (statut « proposition NON RATIFIÉE ») et n'invente **aucune** règle fiscale, légale ou probante
  (CLAUDE.md n°2). Il est la **dernière** des cinq sœurs GED (ADR-0032 à 0036) : 0032 pose le méta-modèle
  et les trois schémas PostgreSQL, 0033 le coffre tiers (5ᵉ axe enfichable, option C), 0034 l'ingestion
  générique, 0035 la recherche/index ; cet ADR-0036 grave le **journal de consultation** — la trace des
  **lectures** qui ferme la confidentialité de 0035 et atteste l'ouverture des paquets de 0033.

- **Numérotation** : ADR-**0036**. La numérotation libre de la GED (F19 §9) commence à **0032** (le repo
  contient déjà DEUX `ADR-0031` — `-cablage-cycle-run-agent…` et `-licence-fluentassertions…`). Plan d'ADR
  GED : **0032** méta-modèle, **0033** coffre tiers/option C (fast-follow GED20), **0034** ingestion
  générique, **0035** recherche & index, **0036** journal de consultation.

- **Contexte décisionnel** : `docs/conception/F19-GED-Dynamique-Coffre-Fort.md` §6.5 (droits &
  confidentialité, masquage server-side), §6.6 (audit applicatif de consultation, base tenant), §7 (règles
  non négociables n°4/9/10), §8 (invariants `INV-GED-NNN`, Definition of Done append-only/confidentialité),
  §11 D8/D9 (points ouverts) ; sources socle/code réelles citées par F19 :
  `src/Modules/Documents/Infrastructure/Migrations/V005__create_archive_entries_table.sql` (pattern WORM
  copié : fonction de rejet + double trigger `BEFORE UPDATE OR DELETE` / `BEFORE TRUNCATE`),
  `Documents/document_events` (audit de **mutations** append-only en base tenant, modèle imité),
  `audit.field_changes` (audit socle partagé via `ISystemConnectionFactory` — à **NE PAS** utiliser),
  `Host/Liakont.Host/Security/RolePermissionCatalog.cs` (matrice de permissions à amender) ; ADR liés :
  ADR-0032 (méta-modèle GED), ADR-0033 (coffre tiers/`'open_archive'`), ADR-0035 (recherche/index : toute
  consultation produit une entrée), ADR-0017 (permissions/claims `RolePermissionCatalog`).

## Contexte

La GED expose un **portail de consultation** (F19 §6.7) : recherche multidimensionnelle, fiche document,
exploration de graphe, export, ouverture de paquet coffre. Toutes ces opérations sont des **lectures**.
Or le socle Liakont possède déjà un audit, mais c'est un audit de **mutations** (`document_events` en base
tenant, `audit.field_changes` socle partagé) : il trace les **écritures**, pas les **accès**. Tracer
*qui a consulté quoi* est un besoin distinct, à valeur potentiellement **probante** (preuve d'accès,
preuve de non-accès à un axe confidentiel, journal d'exploitation RGPD), que ces tables ne couvrent pas et
qu'on ne saurait y rabattre sans confondre deux natures d'événement.

Le piège central — celui que la décision évite — est de router ce journal de consultation vers l'audit
**socle** `audit.field_changes` via `ISystemConnectionFactory`. Cette table est **partagée** (base
système) : y écrire la consultation d'un tenant **mélange les lectures de tous les tenants dans une table
commune** = fuite cross-tenant (CLAUDE.md n°9, règle métier non négociable). Elle confond aussi la
**lecture** avec la **mutation** (sa sémantique : `field_changes`). F19 §6.6 tranche donc explicitement :
table **NEUVE** en **base tenant**, schéma `ged_index`, **routée par `IConnectionFactory`** (la
connexion tenant-scopée), **jamais** par `ISystemConnectionFactory`.

La deuxième force en présence est un arbitrage de robustesse. Une lecture n'a **pas** de transaction
métier à casser : si l'écriture de la trace échoue, faire échouer la lecture serait disproportionné — d'où
un défaut **best-effort + log Warning** (observabilité). MAIS la **motivation** de ce journal diffère de
celle d'un simple log : si le tenant retient une **finalité probante** (D8 — la trace doit pouvoir prouver
un accès devant un tiers), alors un Warning noyé dans les logs est insuffisant, et le régime bascule en
**fail-closed** (refuser l'accès si la trace échoue) ou au minimum **alerte de supervision**. F19 ne
**tranche pas** ce point (D8, owner Sécurité + DPO) ; cet ADR fixe le **défaut défendable** (best-effort)
et grave le mécanisme qui rend fail-closed **activable** sans réécriture, sans **jamais affaiblir en
silence** si le régime probant est retenu (CLAUDE.md n°3).

La troisième force est la **confidentialité** (F19 §6.5). Le masquage des axes confidentiels est
**server-side** (jamais UI) et **étendu à tous les canaux**, y compris le journal : `query_text` ET
`detail` doivent être **masqués/hachés** si l'axe ciblé est confidentiel. Sinon le journal devient un
**canal de contournement** (un opérateur sans le droit `confidential` lirait la valeur recherchée dans le
log : oracle indirect). Le journal **n'est pas un canal de fuite** : le canal ne se déplace pas de l'axe
vers le log (symétrie avec le déplacement axe→graphe traité en §6.4/RL-31).

## Décision

### 1. Table NEUVE `ged_index.consultation_log` en BASE TENANT, routée par `IConnectionFactory`

Le journal de consultation est une **donnée métier tenant-scopée**, au même titre que `document_events` :
elle vit dans la **base du tenant**, schéma `ged_index`, et est **routée par `IConnectionFactory`** (la
connexion résolue pour le tenant courant). Elle est **distincte** de l'audit socle de **mutations**
(`audit.field_changes`, base système partagée, accédé via `ISystemConnectionFactory`) : c'est un journal
de **consultation / lecture**, pas de mutation. Schéma repris **fidèlement** de F19 §6.6 :

```sql
CREATE TABLE IF NOT EXISTS ged_index.consultation_log (
    id            uuid        NOT NULL DEFAULT gen_random_uuid(),
    occurred_utc  timestamptz NOT NULL DEFAULT now(),
    actor_id      text        NOT NULL,
    action        text        NOT NULL,   -- 'search'|'view_document'|'explore_entity'|'export'|'open_archive'
    managed_document_id uuid,
    entity_id     uuid,
    query_text    text,                    -- masqué/haché si axe confidentiel ciblé (§6.5)
    result_count  int,
    detail        jsonb,                   -- critères/facettes, confidentiels masqués
    correlation_id uuid,
    CONSTRAINT pk_consultation_log PRIMARY KEY (id)
);
CREATE INDEX IF NOT EXISTS ix_consultation_actor ON ged_index.consultation_log (actor_id, occurred_utc DESC);
-- triggers reject_*_mutation (UPDATE/DELETE) + no_truncate : COPIE EXACTE du pattern document_events (§3.6).
```

Les cinq valeurs d'`action` sont **fermées** (CHECK applicatif ou en base : `'search'`, `'view_document'`,
`'explore_entity'`, `'export'`, `'open_archive'`). `managed_document_id` / `entity_id` réfèrent (en
**soft-link logique**, sans FK cross-schéma — CLAUDE.md n°9 / F19 §7) les `managed_documents` /
`entity_instances` du méta-modèle (ADR-0032). `correlation_id` relie une recherche à ses ouvertures de
documents subséquentes. **Aucune jointure SQL cross-schéma `ged_* → documents.*`** n'est créée par cette
table (garde lint/grep des migrations, F19 §8).

### 2. APPEND-ONLY / WORM par double trigger — COPIE EXACTE du pattern `document_events` / `archive_entries`

`consultation_log` est **append-only immuable** (CLAUDE.md n°4 : « Piste d'audit … append-only »). On
**copie exactement** le garde-fou éprouvé de `documents.archive_entries`
(`V005__create_archive_entries_table.sql`) : une fonction de rejet `ged_index.reject_consultation_log_mutation()`
levée par **deux** triggers — un trigger de **ligne** `BEFORE UPDATE OR DELETE` et un trigger
d'**instruction** `BEFORE TRUNCATE` :

```sql
CREATE OR REPLACE FUNCTION ged_index.reject_consultation_log_mutation()
    RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'La table ged_index.consultation_log est append-only (WORM) : toute modification ou suppression d''une entrée existante est interdite (CLAUDE.md n.4).';
END;
$$;

CREATE OR REPLACE TRIGGER trg_consultation_log_append_only
    BEFORE UPDATE OR DELETE ON ged_index.consultation_log
    FOR EACH ROW EXECUTE FUNCTION ged_index.reject_consultation_log_mutation();

CREATE OR REPLACE TRIGGER trg_consultation_log_no_truncate
    BEFORE TRUNCATE ON ged_index.consultation_log
    FOR EACH STATEMENT EXECUTE FUNCTION ged_index.reject_consultation_log_mutation();
```

**Point non négociable** : un trigger s'applique à **TOUT rôle** (y compris propriétaire / superuser),
contrairement à un `REVOKE` sans effet sur le propriétaire de la table. Le trigger d'**instruction**
`BEFORE TRUNCATE` est **obligatoire** : un `TRUNCATE` ne déclenche **pas** un trigger de ligne
(`FOR EACH ROW`) — l'omettre laisse un vecteur de purge en masse (faux-vert classique). **Aucun** chemin
d'UPDATE/DELETE/TRUNCATE, **aucune** purge automatique (CLAUDE.md n°4) : la rétention/minimisation est un
**point ouvert** (D8), pas un mécanisme de suppression à graver ici.

### 3. Écriture via `IConsultationAuditWriter` (NEUF, `Ged.Contracts`, tenant-scopé) — best-effort par défaut, fail-closed si finalité probante

L'écriture passe par un port **NEUF** `IConsultationAuditWriter` exposé dans `Ged.Contracts`,
**tenant-scopé** (il n'écrit que dans la base du tenant courant via `IConnectionFactory`). Le **régime de
robustesse** est piloté par un paramètre de **capacité tenant** (paramétrage en base, jamais un
`if (tenant is …)`), avec **deux régimes** :

1. **Régime par DÉFAUT — best-effort + log Warning** (`ConsultationAuditMode.BestEffort`). Une lecture
   n'a **pas** de transaction métier à casser : si l'écriture de la trace échoue, la lecture **réussit**
   et l'échec est **journalisé en Warning** pour l'observabilité (message FR, n° de document/action si
   disponible — CLAUDE.md n°12). C'est le défaut **défendable** (F19 §11 D8 ; aucune valeur probante
   présumée).

2. **Régime PROBANT — fail-closed ou alerte de supervision** (`ConsultationAuditMode.Evidential`). Si le
   tenant **confirme une finalité probante** (D8, owner Sécurité + DPO), l'écriture de la trace devient
   une **précondition de l'accès** : soit **fail-closed** (la lecture est **refusée** si la trace échoue
   — message FR opérateur), soit **au minimum une alerte de supervision** sur échec (réutilise le canal
   d'alerte SUP, **jamais** un Warning noyé). **POINT NON NÉGOCIABLE (CLAUDE.md n°3) :** dans le régime
   probant, on ne **dégrade JAMAIS en silence** vers un simple Warning ; « bloquer plutôt qu'affirmer une
   trace qui n'existe pas ».

Le **choix** du régime vient du **tenant** (paramétrage), **jamais** d'une obligation légale produit
inventée (CLAUDE.md n°2) ni d'un flag doublonnant une capacité (n°8). Tant que D8 n'est pas tranché, le
défaut **best-effort** s'applique ; le code expose le régime `Evidential` **prêt à activer** sans
réécriture (le mécanisme est gravé, son **déclenchement** est paramétrage).

### 4. Confidentialité dans le log : masquage SERVER-SIDE de `query_text` ET `detail` (anti-oracle)

Le journal **n'est pas un canal de contournement** de la confidentialité (F19 §6.5, dernier tiret
« log : `detail` ET `query_text` masqués/hachés si confidentiels »). Le masquage est **server-side** (au
moment de l'écriture, jamais à l'affichage UI) :

- si la recherche/exploration **cible un axe `is_confidential`** (ou un `entity_type`/`relation`
  confidentiel) **sans le droit** correspondant, alors `query_text` est **masqué ou haché** (jamais la
  valeur en clair) et les clés/valeurs confidentielles de `detail` (critères, facettes) sont **masquées**
  avant insertion ;
- le `result_count` et les facettes confidentielles ne doivent pas non plus servir d'**oracle** (un
  compte non-nul révélant l'existence d'une valeur confidentielle masquée) — cohérent avec l'anti-oracle
  des facettes de §6.5 ;
- le canal de fuite **ne se déplace pas** de l'axe (ADR-0035) vers le log : le prédicat de masquage est
  **matérialisé dans le writer**, pas seulement en prose, et **testé en lecture, facette, graphe, export
  ET log** (F19 §8, ligne « Confidentialité »).

**Permissions Liakont dédiées** : la consultation GED est gouvernée par les permissions **NEUVES**
`liakont.ged.read` / `liakont.ged.export` / `liakont.ged.confidential`, à **amender** dans la matrice
`RolePermissionCatalog` (ADR-0017). C'est une **matérialisation en CODE** (`Dictionary`/`const`,
recompilation traçable), **pas** une règle fiscale inventée et **pas** du paramétrage tenant en base ;
**jamais** une permission **socle** accordée à un rôle Liakont (cf. FIX07c, RL-35). **Tant que la matrice
n'est pas amendée, AUCUNE permission GED n'est accordée** — bloquer plutôt qu'inventer (CLAUDE.md n°2).
L'`action='export'` exige `liakont.ged.export` ; l'accès à un axe/entité confidentiel exige
`liakont.ged.confidential` (faute de quoi le masquage du présent §4 s'applique).

### 5. Une consultation = une entrée ; l'`'open_archive'` atteste l'ouverture du paquet

Toute opération du portail (ADR-0035 : « toute recherche/consultation produit une entrée ») écrit **une**
ligne `consultation_log` via `IConsultationAuditWriter` :

- `'search'` à chaque recherche multidimensionnelle (`query_text` masqué selon §4, `result_count`,
  `detail` = critères/facettes masqués) ;
- `'view_document'` à l'ouverture d'une fiche (`managed_document_id`) ;
- `'explore_entity'` à une traversée de graphe (`entity_id`) ;
- `'export'` à un export (sous `liakont.ged.export`) ;
- `'open_archive'` quand le « lien coffre » **ouvre/atteste** un paquet (le lien **ouvre/atteste**, ne
  modifie **jamais** le paquet — formule définie dans ADR-0035, home de l'invariant). L'entrée
  `'open_archive'` est la **preuve d'accès** au paquet scellé ; elle ne déclenche **aucune** écriture sur la
  chaîne fiscale (`archive_entries`), cohérente avec la WORM-neutralité (INV-GED-07 (home ADR-0035)).

L'écriture de cette ligne suit le régime du §3 (best-effort par défaut / fail-closed si probant). Le
journal est en **lecture** sous `liakont.ged.read` (et reste lui-même append-only — on ne purge pas un
journal d'accès).

### 6. Portée : structure, robustesse et confidentialité — aucun code, aucune décision fiscale/probante

Cet ADR **n'écrit aucun code** (livré par les items GEDxx de F19 §10). Il ne tranche **ni** la valeur
**probante** d'une trace de consultation (D8, owner Sécurité + DPO), **ni** la politique de
**rétention/minimisation** RGPD de `actor_id` (D8 — la pseudonymisation IN-PLACE est IMPOSSIBLE sous le WORM
gravé : la mitigation passe par la MINIMISATION À L'ÉCRITURE ou le crypto-shredding par document (D9), JAMAIS
par une mutation a posteriori du log scellé), **ni** le **chiffrement au repos** des
valeurs confidentielles (D9, owner Sécurité). Il **n'introduit aucun mécanisme transverse nouveau** :
réutilise le moule WORM double-trigger (`archive_entries`/`document_events`), `IConnectionFactory`
(tenant-scope), `RolePermissionCatalog`/`PermissionAuthorizationHandler` (ADR-0017), le canal d'alerte
SUP. **Aucun code `Stratum.*` vendored modifié** (F19 §7 règle 11).

## Invariants

- **INV-GED-11** — `consultation_log` est **append-only** et vit en **base tenant** (schéma `ged_index`,
  routée par `IConnectionFactory`, **jamais** `ISystemConnectionFactory`). UPDATE/DELETE/TRUNCATE sont
  **rejetés** par le double trigger (`BEFORE UPDATE OR DELETE` + `BEFORE TRUNCATE`, tout rôle) — prouvé
  par test d'intégration base réelle (F19 §8, ligne « Append-only ») ; aucune purge automatique.

- **INV-GED-10 (home ADR-0035) — rappel :** Confidentialité : masquage server-side sur tous les canaux de
  restitution. Le prédicat (is_confidential = false OR @hasConfidentialRight) est MATÉRIALISÉ DANS LE SQL
  pour la recherche par AXE, la FACETTE et le GRAPHE (RL-31, anti-oracle ; racine de graphe incluse —
  CANON-A). Le PLEIN TEXTE, lui, EXCLUT les axes confidentiels du search_vector partagé : le droit
  liakont.ged.confidential N'OUVRE PAS l'accès FTS en V1 (asymétrie ASSUMÉE — un FTS paramétré par le droit
  serait un index séparé, fast-follow). L'EXPORT et le LOG masquent/excluent les valeurs confidentielles. Le
  chiffrement au repos reste ❓ NON TRANCHÉ (D9).

## Conséquences

**Positif** : la trace des **lectures** GED est gravée **sans confondre** lecture et mutation et **sans
fuite cross-tenant** (base tenant, jamais l'audit socle partagé) ; le WORM réutilise un patron **éprouvé en
production** (`archive_entries`), zéro mécanisme nouveau ; le masquage server-side ferme le **canal de
contournement** que le log aurait pu ouvrir (anti-oracle) ; le régime de robustesse est **paramétrable**
(best-effort par défaut, fail-closed/alerte si probant) **sans réécriture** le jour où D8 est tranché — et
ne s'affaiblit **jamais en silence** sous régime probant (CLAUDE.md n°3) ; les permissions GED sont une
**matérialisation en code traçable** (ADR-0017), jamais une permission socle accordée à un rôle Liakont.

**À la charge du(des) lot(s) d'implémentation** (items GEDxx de F19 §10) : migration `consultation_log`
(table + fonction de rejet `reject_consultation_log_mutation` + triggers `BEFORE UPDATE OR DELETE` et
`BEFORE TRUNCATE`, gabarit `V005` Documents) dans le schéma `ged_index` ; port `IConsultationAuditWriter`
(`Ged.Contracts`, tenant-scopé) + implémentation `Ged.Infrastructure` routée par `IConnectionFactory` ;
les **deux régimes** de robustesse (`BestEffort` / `Evidential`) pilotés par capacité tenant, avec **test
de chaque branche** (best-effort : lecture réussit + Warning ; probant : refus ou alerte sur échec de
trace) ; masquage server-side de `query_text` + `detail` **testé** (recherche d'un axe confidentiel sans
droit ⇒ valeur jamais en clair dans le log) ; émission d'une entrée pour **chacune** des cinq `action`
(dont `'open_archive'` sans toucher `archive_entries`) ; consomme les permissions amendées par GED06 dans
`RolePermissionCatalog` (`liakont.ged.read`/`.export`/`.confidential`) avec test prouvant qu'**aucune** permission GED n'est
accordée tant que la matrice n'est pas amendée, et qu'**aucune** permission **socle** n'est accordée à un
rôle Liakont ; test d'**isolation cross-tenant** (consultation du tenant B invisible depuis le tenant A,
≥ 2 bases) ; test confirmant **l'absence** de jointure SQL cross-schéma `ged_* → documents.*` (lint/grep
des migrations & queries `Ged`, F19 §8).

**Limite** : cet ADR ne grave **ni** la valeur probante d'une trace de consultation (D8), **ni** la
politique RGPD de rétention/minimisation/pseudonymisation (D8), **ni** le chiffrement au repos des valeurs
confidentielles (D9). Il ne fixe **aucune** rétention : aucune purge n'est codée (append-only intégral).

### Points NON TRANCHÉS (F19 §11 — défaut défendable pris, l'owner tranche, jamais inventé)

| # | Point | Défaut défendable PRIS | Owner |
|---|---|---|---|
| D8 | `consultation_log` : best-effort **ou** fail-closed si finalité probante ; rétention/minimisation RGPD | Best-effort par défaut ; fail-closed **+ alerte** si probant. La pseudonymisation IN-PLACE d'`actor_id` est IMPOSSIBLE sous le WORM gravé (un UPDATE est rejeté par le trigger append-only, exactement comme la purge) : la mitigation RGPD passe par la MINIMISATION À L'ÉCRITURE (ne pas écrire d'identifiant ré-identifiant au-delà du nécessaire) ou par le crypto-shredding par document (D9 — prérequis « chiffrement-au-repos » ABSENT aujourd'hui), JAMAIS par une mutation a posteriori du log scellé. Aucune purge auto (append-only intégral). | Sécurité + DPO |
| D9 | **Chiffrement au repos** des valeurs d'axes `is_confidential` (par extension, des valeurs confidentielles éventuellement présentes dans `detail`/`query_text`) | ❓ NON TRANCHÉ — poser explicitement (cohérent règle 10) ; à arbitrer selon sensibilité ; en attendant, masquage/hachage server-side sert de défense | Sécurité |

Aucun de ces points ne stalle le dev : le régime probant et le chiffrement au repos sont des **capacités
paramétrables prêtes à activer**, pas des gates de livraison du journal lui-même. Le **défaut** (best-effort
+ masquage server-side + append-only WORM) est livrable et testable immédiatement ; l'owner tranche le
régime probant (D8) et le chiffrement (D9) sans réécriture du schéma.

## Alternatives rejetées

- **Écrire la consultation dans l'audit socle `audit.field_changes` via `ISystemConnectionFactory`** :
  cette table est **partagée** (base système) → mélanger les lectures de tous les tenants = **fuite
  cross-tenant** (CLAUDE.md n°9) ; et elle confond **mutation** (`field_changes`) avec **lecture**.
  **Rejetée** — table NEUVE `ged_index.consultation_log` en **base tenant**, routée par
  `IConnectionFactory` (F19 §6.6).
- **Fail-closed inconditionnel** (refuser toute lecture si la trace échoue) : casserait une lecture **sans
  valeur probante** (une lecture n'a pas de transaction métier à protéger) — disproportionné. **Rejetée**
  — best-effort par défaut, fail-closed **conditionné** à la finalité probante (D8).
- **Warning noyé même en régime probant** : sous finalité probante, un échec de trace silencieusement
  ravalé en Warning **affaiblit en silence** une garantie d'audit (CLAUDE.md n°3). **Rejetée** — régime
  probant ⇒ fail-closed **ou** alerte de supervision, jamais un Warning noyé.
- **`query_text` confidentiel stocké en clair dans le log** : le journal deviendrait un **oracle** (un
  opérateur sans `liakont.ged.confidential` lirait la valeur recherchée dans le log) — le canal de fuite se
  déplacerait de l'axe vers le log. **Rejetée** — masquage/hachage **server-side** de `query_text` ET
  `detail` (INV-GED-10).
- **Réutiliser `document_events` (audit de mutations) comme journal de consultation** : confond deux
  natures d'événement (écriture vs accès), pollue la sémantique de l'audit de mutations et ne porte pas
  l'`action` de consultation. **Rejetée** — table dédiée, sémantique de **lecture**, mêmes garanties WORM.

## Références

- `docs/conception/F19-GED-Dynamique-Coffre-Fort.md` §6.5 (droits & confidentialité, masquage server-side),
  §6.6 (audit applicatif de consultation, base tenant — schéma SQL repris), §6.7 (portail), §7 (règles non
  négociables n°4/9/10), §8 (invariants `INV-GED-NNN`, Definition of Done append-only/confidentialité),
  §11 D8/D9.
- `docs/adr/ADR-0032-meta-modele-ged-axes-entites-polymorphes.md` (méta-modèle GED :
  `managed_documents` / `entity_instances` référencés en soft-link) ;
  `docs/adr/ADR-0033-coffre-probant-tiers-sae-5e-axe-option-c.md` (`'open_archive'` atteste le paquet,
  jamais une mutation de la chaîne fiscale) ;
  `docs/adr/ADR-0035-recherche-index-ged-tsvector.md` (toute recherche/consultation
  produit une entrée ; confidentialité server-side) ;
  `docs/adr/ADR-0017-pont-role-permission-claims-oidc.md` (matrice `RolePermissionCatalog` à
  amender ; jamais une permission socle accordée à un rôle Liakont — cf. FIX07c, RL-35).
- Patrons réels imités / à NE PAS utiliser : `src/Modules/Documents/Infrastructure/Migrations/V005__create_archive_entries_table.sql`
  (pattern WORM **copié** : fonction de rejet + double trigger `BEFORE UPDATE OR DELETE` / `BEFORE TRUNCATE`)
  et `Documents/document_events` (audit de **mutations** append-only en base tenant, modèle imité) ;
  `audit.field_changes` via `ISystemConnectionFactory` (base système **partagée** — à **NE PAS** utiliser
  pour la consultation, fuite cross-tenant) ; `Host/Liakont.Host/Security/RolePermissionCatalog.cs`
  (matrice à amender, §6.5).
- Règlement (UE) 2016/679 (RGPD), art. 17 (droit à l'effacement) et minimisation : cités par F19 §11 D3/D8
  comme **ouverts** (owner DPO) — aucune règle de rétention n'est tranchée ici.
