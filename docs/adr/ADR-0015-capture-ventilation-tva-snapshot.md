# ADR-0015 — Capture d'un snapshot requêtable de la ventilation TVA pour l'e-reporting de paiement

**Date :** 2026-06-06

**Statut :** Proposé (à confirmer par l'opérateur — comme ADR-0014)

**Complète :** ADR-0014 (staging durable du contenu pivot à l'intake). Prérequis d'architecture de
**PIP03a** (e-reporting de paiement, partie constructible). N'introduit **aucune règle fiscale** :
le snapshot ne porte que la ventilation **déjà sourcée** par le mapping TVA validé.

---

## Contexte

L'e-reporting de paiement (F09, lot PIP) agrège les **encaissements par jour × taux de TVA** au
niveau SIREN. Pour ventiler un encaissement par taux, le moteur a besoin de la **ventilation TVA**
(base HT et TVA **par taux**) du ou des documents rattachés au règlement.

Or cette ventilation n'est persistée dans **aucune table requêtable** une fois le document émis :

- elle vit dans le **staging** (`IPayloadStagingStore`, ADR-0014), qui est **purgé après `Issued`**
  dès que le paquet WORM est écrit — donc absent à l'heure du job d'agrégation ;
- elle vit dans le **blob WORM** d'archive (`IArchiveStore`), **non requêtable** par une API de
  domaine (immuable, conçu pour la preuve, pas pour l'interrogation analytique) ;
- la table `documents.documents` ne porte que les **totaux agrégés** du document (pas la ventilation
  par taux), et le `PivotPayment` (`{ PaymentDate, Amount, Method, RelatedDocumentNumber,
  SourceReference }`) **ne porte pas le taux**.

Conséquence : un **job d'agrégation planifié** — qui s'exécute potentiellement **longtemps après**
l'émission et **après la purge du staging** — ne peut **pas** décomposer un paiement par taux à
partir de données requêtables. C'est le trou d'architecture qui a bloqué PIP03 (obstacle #3 du
session-log `orch-20260606-050619-s55900_PIP03.md`). F09 et `orchestration/items/PIP.yaml` ne
spécifiaient pas **où** ni **quand** capturer cette ventilation.

## Décision

Capturer, **au moment du CHECK** (quand la ventilation TVA **sourcée et validée** est calculée par
`ITvaMappingService`, juste avant `MarkReadyToSend`), un **snapshot** de la ventilation par taux du
document dans une **persistance dédiée, tenant-scopée, requêtable, append-only**, **distincte** du
staging et du coffre WORM.

1. **Contenu du snapshot** — par `document_id` + `source_reference`, les lignes
   `{ Rate, TaxableBase (HT), VatAmount }` en **`decimal`** (jamais float), plus la
   `mapping_version` sous laquelle elles ont été calculées (provenance) et l'`operationCategory` du
   document. **Le snapshot ne porte que la sortie du mapping validé** — aucune dérivation inventée.

2. **Ce que le snapshot NE fait PAS** — il **ne fabrique pas** le découpage **part frais /
   adjudication** d'un document `Mixte` (enchères) : cette classification reste une **décision
   fiscale non sourcée** (D-b, ADR-0004 / F03 §2.3), traitée en **PIP03b**. Le snapshot capture la
   ventilation **par taux** telle que produite par le mapping ; il rend cette donnée **requêtable**,
   sans la compléter. Pour un document **mono-catégorie** (p. ex. pure prestation de services), la
   ventilation par taux est **directement** utilisable par l'agrégation de paiement (PIP03a) ; pour
   un `Mixte`, l'e-reporting de paiement reste **suspendu** tant que D-b n'est pas tranchée.

3. **Durée de vie / rapport à la purge ADR-0014** — le snapshot est une **projection durable et
   légère** (quelques lignes par document), **EXEMPTE de la purge du staging** : c'est précisément
   parce que le staging est purgé après `Issued` qu'il faut une projection qui **survit** pour
   l'agrégation post-émission. Le snapshot **n'est ni le staging** (transitoire/purgeable) **ni le
   coffre WORM** (immuable/preuve) : c'est une **troisième persistance**, une projection requêtable.
   La purge ADR-0014 reste **inchangée**.

4. **Immutabilité (CLAUDE.md n°4)** — le snapshot est **append-only**, versionné par
   `mapping_version`. Un document re-évalué (re-mapping après revalidation de la table TVA) **ajoute**
   une nouvelle version, **n'écrase jamais** la précédente. L'agrégation de paiement utilise la
   version liée à l'émission du document (`Document.MappingVersion`, posée au passage `ReadyToSend`
   par PIP01a) — happened-before garanti.

5. **Frontière (CLAUDE.md n°6)** — la persistance est **tenant-scopée** ; elle est écrite au CHECK
   (module Pipeline) et lue par l'agrégateur de paiement (module Pipeline). L'implémentation peut la
   loger dans le module **Pipeline** (propriétaire, écrit au CHECK, lu par le job — couplage minimal)
   ou l'exposer via `Documents.Contracts` ; dans les deux cas l'accès **cross-module passe par les
   Contracts** (NetArchTest), jamais en SQL brut sur la table d'un autre module.

## Invariants

- **INV-VENTILATION-001** — Le snapshot ne contient que la ventilation **issue du mapping validé** ;
  aucune valeur n'est dérivée ni devinée (P1 si violé — règle fiscale inventée).
- **INV-VENTILATION-002** — Montants en **`decimal`**, jamais float/double.
- **INV-VENTILATION-003** — **Append-only**, versionné par `mapping_version` ; aucun chemin
  d'update/delete (P1 si violé — piste d'audit immuable).
- **INV-VENTILATION-004** — **Tenant-scopé** : un tenant ne lit jamais le snapshot d'un autre.
- **INV-VENTILATION-005** — Le snapshot **ne classe pas** frais/adjudication d'un `Mixte` (réservé à
  PIP03b, gelé sur D-b) ; un `Mixte` reste **suspendu** pour l'e-reporting de paiement.
- **INV-VENTILATION-006** — Persistance **distincte** du staging (purgé) et du coffre WORM
  (immuable) ; la purge ADR-0014 n'efface jamais le snapshot.

## Conséquences

- **Positif** — l'agrégation de paiement par taux devient possible à partir de **données
  requêtables**, **après** la purge du staging. Aucun allongement de la durée de vie du staging
  (timing de purge ADR-0014 inchangé). Empreinte faible.
- **Coût** — une nouvelle table + l'extension du CHECK (PIP01b) pour écrire le snapshot de façon
  additive et idempotente (clé `document_id` + `mapping_version`).
- **Ne résout pas D-a** (cadence/fenêtrage de période) **ni D-b** (découpage Mixte) : ces règles
  fiscales restent en **PIP03b**. ADR-0015 ne débloque que la **requêtabilité** de la ventilation,
  ce qui rend **PIP03a** constructible (agrégation des documents mono-catégorie + sémantique de
  suspension + chemin Fake), sans inventer la moindre règle.

## Liens

- ADR-0014 (staging durable, purge subordonnée au WORM) — la projection survit à la purge.
- ADR-0007 (sérialisation canonique du pivot) — source de la ventilation au CHECK.
- ADR-0004 / F03 §2.3 — découpage frais/adjudication **non sourcé** (hors périmètre de cet ADR).
- F09 §5 (modèle d'agrégation paiement), F12-A §3 (paramètres fiscaux tenant).
- `orchestration/items/PIP.yaml` : PIP03a (consomme cet ADR), PIP03b (fast-follow, D-a/D-b).
