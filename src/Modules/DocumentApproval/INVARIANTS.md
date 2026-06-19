# Invariants — module `Liakont.Modules.DocumentApproval` (ADR-0028)

Les invariants sont **normatifs** ; aucun n'est garanti tant qu'il n'est pas livré **et** prouvé par test.
Statut : ceux marqués ✅ sont livrés et testés par **SIG04** ; les autres relèvent des items suivants.

- **INV-APPROVAL-1** ✅ — Module racine `Liakont.Modules.DocumentApproval` (non collisionnant avec
  `Liakont.Modules.Validation`). Couplage à l'émission par **ports** (un par purpose) — exposés en **SIG06**,
  jamais une dépendance concrète (NetArchTest).
- **INV-APPROVAL-2** ✅ — Machine **FERMÉE** à **7 états distincts** (`Rejected` ≠ `Contested`) ; toute
  transition hors graphe est rejetée (test produit cartésien) ; **aucun retour arrière** depuis un terminal ;
  `PendingValidation` a des arêtes directes vers les terminaux et `ValidationInProgress` est un intermédiaire
  optionnel.
- **INV-APPROVAL-3** ✅ — Chaque `ValidationPurpose` déclare un **sous-graphe autorisé explicite** ; le
  sous-graphe `SelfBilledAcceptance` = 4 états `{PendingValidation, Validated, TacitlyValidated, Contested}`
  (= ADR-0024 ; `ValidationInProgress`/`Expired`/`Rejected` rejetés par la garde de purpose — test). Re-preuve
  du cartésien INV-ACCEPT-4 sur le purpose.
- **INV-APPROVAL-4** ✅ — **RÈGLE DE GATE** (fonction pure `ApprovalGate`) : ouvre **ssi** (1) état ∈
  `{Validated, TacitlyValidated}` **ET** (2) niveau de preuve ≥ exigence tenant (**par slot** ; `Recorded` nu ne
  franchit pas AES/QES ; tacit ne satisfait que `Recorded`) **ET** (3) self-billing **sur `Validated` expresse
  uniquement** : acceptation enregistrée explicite. Condition 3 **non appliquée** à `TacitlyValidated`. Gate ni
  durci ni affaibli au nom d'une obligation inexistante (le câblage tenant/pipeline est SIG06).
- **INV-APPROVAL-5** ✅ — Ré-essai par nouvel **`attempt`** réservé aux purposes **SIGNATURE** ; **index unique
  partiel `(company_id, document_id, validation_purpose)` sur les non-terminaux** (≤ 1 non terminale) ; **garde
  anti-race** à la création d'un `attempt` (l'`attempt` N doit être un échec terminal — `Expired`/`Rejected` —
  vérifié sous verrou, MÊME transaction ; test de concurrence) ; le gate lit la tentative **la plus récente**.
  **Self-billing EXCLU** : `Contested` définitif, correction = avoir 261 + nouveau `document_id`.
- **INV-APPROVAL-6** ✅ — **Toute** transition (création incluse) écrit une ligne **`document_approval_log`**
  dans la **MÊME transaction** ; journal immuable par **double trigger** (UPDATE/DELETE/TRUNCATE rejetés) et
  **tenant-scopé** (`company_id NOT NULL`). Un événement tardif depuis un terminal est rejeté par la machine
  fermée (et le ré-essai/idempotence vivent au-dessus, SIG07).
- **INV-APPROVAL-7** ✅ — Agrégation N-parties par **slots identifiés idempotents par `SignerId`** (jamais un
  compteur) ; complétude = **tous** les slots distincts remplis ; **niveau de preuve évalué PAR slot** ; un slot
  **refusé** → terminal **immédiat** (`Rejected`, ou `Contested` pour un purpose à sens fiscal). Tests : doublon
  d'un signataire n'ouvre jamais le gate ; slot sous-niveau n'ouvre jamais le gate ; slot refusé bascule
  immédiatement.
- **INV-APPROVAL-8** ✅ (frontière de SIG04) — `DocumentApproval` référence `Signature.Contracts`
  (niveau de preuve) mais **jamais** `Signature.Domain/.Application/.Infrastructure` ni un plug-in, ni un autre
  module métier (NetArchTest + scan déclaratif des `.csproj`). L'arête WORM (job de drain → `Archive.Contracts`)
  est livrée par SIG07.
