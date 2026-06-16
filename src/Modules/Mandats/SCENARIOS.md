# Scénarios de test — module Mandats (MND01, MND02, MND05)

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

## Unit (MND05) — `MandatSequenceTests`

- **Start** démarre à 1 avec le préfixe déclaré ; rejette un préfixe / tenant / mandant absent (aucun défaut inventé, INV-MANDATS-5).
- **Allocate** rend `préfixe + valeur` et avance `NextValue` d'exactement 1 (continuité §1.4, INV-BT1-4).
- **Format** = concaténation `InvariantCulture`, sans séparateur de milliers ni zéro de remplissage inventé.
- **bigint** (INV-BT1-4) : une séquence reconstituée au-delà d'`Int32.MaxValue` alloue sans débordement (jamais float/int32).

## Integration (MND05) — `MandatNumberAllocatorIntegrationTests`

- **Première allocation** : `préfixe + 1` retourné ET assigné à `self_billed_acceptances.allocated_number` (HORS payload
  hashé, INV-BT1-1) ; la séquence (`bigint`) avance à 2.
- **Idempotence sur la clé source** (INV-BT1-2) : un double appel (même document) relit le même numéro ; un seul numéro
  consommé ; une seule ligne d'allocation.
- **Ré-extraction** (INV-BT1-2) : un document DIFFÉRENT de même `source_reference` relit le même BT-1 (assigné aussi à sa
  propre acceptation) ; la séquence n'avance pas.
- **Indépendance par mandant** (INV-BT1-4) : deux mandants ont chacun leur séquence (départ à 1) et leur préfixe.
- **Verrou par mandant** (INV-BT1-4) : deux allocations CONCURRENTES d'un même mandant sont sérialisées → deux numéros
  consécutifs DISTINCTS, jamais un doublon ; la séquence avance sans trou.
- **Isolation tenant** (INV-BT1-4) : l'allocation de la société A n'est jamais visible sous la société B.
- **Immuabilité du registre** : `UPDATE`/`DELETE`/`TRUNCATE` sur `mandat_number_allocations` rejetés par trigger base
  (`PostgresException` « immuable ») ; le numéro alloué reste intact.
- **Fail-closed** (CLAUDE.md n°3) : `source_reference` vide ⇒ `ArgumentException` (INV-BT1-3 partiel) ; mandant inconnu
  ⇒ `InvalidOperationException` ; acceptation absente ⇒ `InvalidOperationException` ET rien consommé (transaction annulée).

## Unit (MND05) — `CanonicalJsonRulesTests` (Agent.Contracts)

- **INV-BT1-1** : en 389, le canonique garde `Number` = identifiant source, sans aucun numéro fiscal alloué ; le booléen
  `IsSelfBilled` est le SEUL différenciateur 389/standard (aucune branche de format, ADR-0007 préservé).
