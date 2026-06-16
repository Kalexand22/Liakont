# Module `Liakont.Modules.DocumentApproval`

Workflow de **validation de document générique et réutilisable** (ADR-0028, F17 §3/§4). Un même mécanisme
sert plusieurs *purposes* : acceptation d'auto-facture (389), signature de mandat, acceptation d'avoir (261),
approbation de relevé d'avancement, circuit comptable multi-paliers, document co-signé N parties.

Nom **non collisionnant** avec le module `Validation` existant (règles EN 16931) — choix tranché en ADR-0028 §1
pour qu'aucune règle NetArchTest / filtre DI ciblant `Liakont.Modules.Validation*` n'attrape ce module.

## Couches (pattern Stratum)

| Couche | Rôle | Livré par SIG04 |
|---|---|---|
| `Contracts` | `ValidationPurpose` (clé de couplage), DTOs de lecture, `IDocumentApprovalQueries` | ✅ |
| `Domain` | Agrégat `DocumentValidation`, machine fermée 7 états, sous-graphes par purpose, slots, règle de gate | ✅ |
| `Application` | `IDocumentValidationUnitOfWork` (transition + journal dans la MÊME transaction) | ✅ |
| `Infrastructure` | Migrations DbUp (schéma `documentapproval`), UoW Postgres, requêtes, enregistrement DI | ✅ |
| `Web` | Point de montage console (UI signature = SIG10) | mount point vide |

## Périmètre de SIG04 (cœur générique) — ce qui N'est PAS ici

- **Ports par purpose** (`ISelfBilledGate` réutilisé, `IMandateSignatureGate`, `ICreditNoteAcceptanceGate`,
  `IMultiPartySignatureGate`) + câblage pipeline : **SIG06**.
- **Refactor de `SelfBilledAcceptance` (Mandats)** pour déléguer à ce module : **SIG05**.
- **Jobs `TenantJobRunner`** (bascule tacite / timeout) et **drain WORM** (`Archive.Contracts`) : SIG06/SIG07.

SIG04 livre le **mécanisme** (agrégat + machine + slots + `attempt` + index partiel + garde anti-race + journal
append-only + règle de gate comme fonction pure) et sa **persistance**, prouvés par tests unit + intégration.

## Machine fermée (ADR-0028 §3) — 7 états DISTINCTS

`PendingValidation` (initial) a des **arêtes directes** vers tous les terminaux (chemin `Recorded`/synchrone) ET
vers `ValidationInProgress` (intermédiaire OPTIONNEL, purposes signature async). `Rejected` (refus signature) et
`Contested` (refus self-billing, sens fiscal : avoir 261) sont **deux états séparés** — aucun purpose n'utilise
les deux. Aucun retour arrière depuis un terminal.

Chaque `ValidationPurpose` déclare un **sous-graphe autorisé explicite** (garde de purpose). Le sous-graphe
`SelfBilledAcceptance` = `{PendingValidation, Validated, TacitlyValidated, Contested}` (= machine ADR-0024
EXACTE). Le graphe complet est l'**union** des purposes ; aucun purpose n'accède à toutes les arêtes.

## Règle de gate (ADR-0028 §5) — fonction pure `ApprovalGate`

Le gate ouvre **ssi** : (1) état ∈ `{Validated, TacitlyValidated}` **ET** (2) niveau de preuve ≥ exigence tenant
(par slot pour le N-parties ; `Recorded` nu ne franchit pas AES/QES ; un `TacitlyValidated` ne satisfait que
`Recorded`) **ET** (3) pour le self-billing **sur transition `Validated` expresse uniquement**, acceptation
enregistrée explicite. La condition 3 **ne s'applique PAS** à `TacitlyValidated`. Le niveau requis est un
**paramétrage tenant** (jamais une obligation produit) — câblé en SIG06.

## Frontières (NetArchTest, ADR-0028 §9)

`DocumentApproval → Signature.Contracts` (niveau de preuve) — JAMAIS `Signature.Domain/.Infrastructure` ni un
plug-in. Aucune dépendance vers un autre module métier. Le journal de drain WORM (SIG07) passera par
`Archive.Contracts`, jamais `Archive.Domain`.
