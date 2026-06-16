# Scénarios de test — module Mandats (MND01, MND02)

Référence les invariants de `INVARIANTS.md`. Niveaux : **Unit** (domaine pur, sans base) et
**Integration** (Testcontainers PostgreSQL réel).

## Unit — `MandantTests`, `MandatTests`, `MandatsBoundaryTests`

- **Mandant.Create** rejette une référence / raison sociale / SIREN / préfixe absent (aucune valeur par
  défaut inventée, INV-MANDATS-5) ; `SellerVatNumber` reste facultatif (BT-31 nullable).
- **Mandat.Create** naît « NON VALIDÉE » et donc 389 suspendu (INV-MANDATS-4/6) ; rejette une référence /
  clause absente ; rejette un délai de contestation ≤ 0 (null = suspendu, jamais un délai nul inventé).
- **Mandat.IsSelfBillingSuspended** — produit cartésien (INV-MANDATS-4) : suspendu si l'une des
  conditions vaut (statut null, délai null, non validé, révoqué) ; actif uniquement si statut renseigné
  ET délai renseigné ET validé ET non révoqué.
- **Mandat.UpdateTerms** repasse « NON VALIDÉE » (`Invalidate`, INV-MANDATS-6) ; refusé sur un mandat révoqué.
- **Mandat.Validate** exige une identité de valideur ; refusé sur un mandat révoqué.
- **Mandat.Revoke** suspend 389 ; révoquer un mandat déjà révoqué est une erreur (jamais un no-op silencieux).
- **MandatsBoundaryTests** (INV-MANDATS-2) : les `.csproj` du module ne référencent que le module + Common +
  Agent.Contracts ; `Mandats.Contracts` ne référence aucun autre module métier ni la persistance.

## Integration — `MandantPersistenceIntegrationTests`, `MandatLifecycleIntegrationTests`

- **Round-trip mandant/mandat** : insertion puis relecture de tous les champs (y compris `SellerVatNumber`
  null, `AssujettissementStatus`/`ContestationDelay` null), via `IMandatsQueries`.
- **Isolation tenant** (INV-MANDATS-1) : une lecture/mutation sur la société A ne voit ni ne touche la
  société B (prouvé sur ≥ 2 `company_id`).
- **Conflit d'unicité** : insérer un mandant (resp. mandat) de même clé pour le même tenant lève `ConflictException`.
- **Journal append-only** (INV-MANDATS-3) : `UPDATE` / `DELETE` / `TRUNCATE` sur `mandat_change_log` sont
  rejetés par le trigger base (`PostgresException` « append-only ») ; l'entrée reste intacte.
- **Atomicité mutation + journal** (INV-MANDATS-3) : une transaction abandonnée (pas de commit) ne laisse
  RIEN — ni mutation, ni entrée de journal.
- **Invalidation + journalisation** (INV-MANDATS-6) : `UpdateTerms` sur un mandat validé le repasse « NON
  VALIDÉE » et écrit une entrée `UpdateMandat` ; `Validate`/`Revoke` écrivent leurs entrées dédiées.
- **Suspendu-par-défaut persisté** (INV-MANDATS-4) : un mandat avec statut/délai null relu reste suspendu.

## Unit (MND02) — `SelfBilledAcceptanceTests`

- **Create** naît `PendingAcceptance` (gate fermé, non terminal), `AllocatedNumber` null (alloué par MND05) ;
  rejette company/document vides ; rejette une échéance antérieure à `PendingSince`.
- **Machine fermée** (INV-ACCEPT-4) — produit cartésien (4 états × 3 transitions) : une transition n'est
  permise QUE depuis `PendingAcceptance` et mène à l'état attendu ; depuis tout état terminal elle est
  rejetée (`InvalidOperationException`) et l'état reste inchangé.
- **`IsAccepted`** (gate ouvert) vaut vrai pour `Accepted`/`TacitlyAccepted`, faux pour `Pending`/`Contested`.

## Integration (MND02) — `SelfBilledAcceptanceIntegrationTests`

- **Round-trip** de l'acceptation `Pending` (état, `DeadlineUtc`, `AllocatedNumber` null) via `ISelfBilledAcceptanceQueries`.
- **Genèse journalisée** (INV-ACCEPT-5) : la création écrit une ligne `from = null → Pending`.
- **Transition journalisée en MÊME transaction** (INV-ACCEPT-5) : acceptation expresse (avec opérateur),
  bascule tacite (transition **système**, sans opérateur), contestation — chacune écrit sa ligne `from → to`.
- **Conflit** : une seconde insertion pour le même `(company_id, document_id)` lève `ConflictException`.
- **Journal append-only** (INV-ACCEPT-5) : `UPDATE`/`DELETE`/`TRUNCATE` sur `self_billed_acceptance_log`
  rejetés par trigger base ; l'entrée reste intacte.
- **Atomicité** : une transaction abandonnée (pas de commit) ne laisse RIEN (ni transition, ni ligne de journal).
- **Isolation tenant** (INV-MANDATS-1) : une transition sur la société A ne touche ni l'état ni le journal de B (≥ 2 sociétés).
