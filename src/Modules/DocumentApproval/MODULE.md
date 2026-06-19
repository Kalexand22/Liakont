# Module `Liakont.Modules.DocumentApproval`

Workflow de **validation de document générique et réutilisable** (ADR-0028, F17 §3/§4). Un même mécanisme
sert plusieurs *purposes* : acceptation d'auto-facture (389), signature de mandat, acceptation d'avoir (261),
approbation de relevé d'avancement, circuit comptable multi-paliers, document co-signé N parties.

Nom **non collisionnant** avec le module `Validation` existant (règles EN 16931) — choix tranché en ADR-0028 §1
pour qu'aucune règle NetArchTest / filtre DI ciblant `Liakont.Modules.Validation*` n'attrape ce module.

## Couches (pattern Stratum)

| Couche | Rôle | Livré par |
|---|---|---|
| `Contracts` | `ValidationPurpose` (clé de couplage), DTOs de lecture, `IDocumentApprovalQueries`, `IDocumentApprovalWorkflow` (commande), `IDocumentApprovalGate` (lecture de la Règle de gate) + `IDocumentApprovalRequirements` (niveau requis tenant) | SIG04 / SIG05 / **SIG06** |
| `Domain` | Agrégat `DocumentValidation`, machine fermée 7 états, sous-graphes par purpose, slots, règle de gate (`ApprovalGate`) | SIG04 |
| `Application` | `IDocumentValidationUnitOfWork` (transition + journal dans la MÊME transaction) | SIG04 |
| `Infrastructure` | Migrations DbUp (schéma `documentapproval` ; V005 = `document_approval_requirement`, SIG06), UoW Postgres, requêtes, câblage du gate, enregistrement DI | SIG04 / **SIG06** |
| `Web` | Point de montage console (UI signature = SIG10) | mount point vide |

## Découpage par item

- **SIG04** : le **cœur générique** (agrégat + machine + slots + `attempt` + index partiel + garde anti-race +
  journal append-only + règle de gate comme fonction pure) et sa **persistance**.
- **SIG05** : refactor de `SelfBilledAcceptance` (Mandats) pour déléguer à ce module.
- **SIG06** : **câblage de la Règle de gate de bout en bout** — `IDocumentApprovalGate` (lit la tentative la plus
  récente, résout le niveau requis tenant, applique `ApprovalGate`) + `IDocumentApprovalRequirements` (paramétrage
  tenant du niveau eIDAS requis par purpose, table `document_approval_requirement` V005, défaut `Recorded`). Les
  **ports par purpose** (`ISelfBilledGate` réutilisé, `IMandateSignatureGate`, `ICreditNoteAcceptanceGate`,
  `IMultiPartySignatureGate`) sont exposés par **`Mandats.Contracts`** et délèguent à `IDocumentApprovalGate`.
- **SIG07** : plug-in Yousign + jobs `TenantJobRunner` (bascule tacite / timeout) + drain WORM (`Archive.Contracts`).

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
**paramétrage tenant** (jamais une obligation produit) — câblé en SIG06 via `IDocumentApprovalGate` (qui résout le
niveau requis du tenant, défaut `Recorded`, puis applique `ApprovalGate`). Un tenant en `Recorded` n'est jamais
bloqué du seul fait de l'absence de fournisseur de signature.

## Frontières (NetArchTest, ADR-0028 §9)

`DocumentApproval → Signature.Contracts` (niveau de preuve) — JAMAIS `Signature.Domain/.Infrastructure` ni un
plug-in. Aucune dépendance vers un autre module métier. Le journal de drain WORM (SIG07) passera par
`Archive.Contracts`, jamais `Archive.Domain`.
