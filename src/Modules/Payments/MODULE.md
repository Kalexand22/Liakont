# Module Payments

## Purpose

Détient les **encaissements bruts** (e-reporting de paiement, F09) et leurs **agrégats jour × taux** avec leur
**piste d'audit immuable**. Pour les prestations de services, la TVA est exigible à l'**encaissement** : la
DGFiP attend la date et le montant encaissé **agrégé par jour et par taux** au niveau SIREN (aucune donnée
nominative — F09 §2). Un agrégat transmis à la DGFiP a la **même valeur fiscale** qu'un document émis : sa
transmission est journalisée de façon **append-only** (`PaymentAggregateEvent`), comme `DocumentEvent`.

**Périmètre de l'item TRK04 (volet Payments)** : le **modèle** et la **persistance** —
- `Payment` : encaissement brut reçu de l'agent (depuis `PivotPaymentDto`, projeté par `PivotPaymentMapper`),
  conservé tel qu'extrait par la source (montant `decimal`, sans interprétation fiscale).
- `PaymentAggregate` : l'agrégat jour × taux (période déclarative, jour, taux, base taxable, TVA, état de
  transmission). Le **CALCUL d'agrégation** (somme des paiements par jour et taux) arrive avec le pipeline
  (**PIP03**) ; ce module porte le modèle, pas le calcul.
- `PaymentAggregateEvent` : la piste d'audit **append-only** de l'agrégat — chaque transmission journalise le
  payload envoyé, la réponse PA, l'horodatage et l'état.
- Le **schéma** PostgreSQL (3 tables : `payments`, `payment_aggregates`, `payment_aggregate_events`), l'**unité
  de travail** Dapper (création idempotente, upsert d'agrégat, read-modify-write transactionnel pour les
  transitions de transmission, ajout d'événement append-only) et les **lectures** pour l'API/console.

Sont **hors périmètre TRK04** : l'**extraction** des règlements (côté agent/adaptateur), le **calcul
d'agrégation** et la **transmission** réelle (pipeline PIP03 + plug-in PA), la **méthode d'imputation** des
frais (paramètre de tenant validé par l'expert-comptable — F09 amendement D2), la **fréquence déclarative**
(paramétrage tenant `reportingFrequency`). V1 = **Flux 10.4 domestique uniquement** (F09 amendement D2 ;
aucune dérivation domestique/international ici).

## Boundaries

- **Schéma owné** : `payments` (PostgreSQL, base **par tenant**).
  - `payments` : l'encaissement brut (date, montant NUMERIC, moyen, référence). Rétention 10 ans, jamais purgé.
  - `payment_aggregates` : l'agrégat jour × taux (période, date, taux NUMERIC(6,4), base/TVA NUMERIC(18,2), état).
  - `payment_aggregate_events` : piste d'audit **append-only** des transmissions d'agrégat (cœur de la
    non-altération, F06 §3 / F09).
- **Isolation tenant** : **par la CONNEXION** — la connexion EST le tenant (database-per-tenant, blueprint §7).
  Aucune colonne de tenant, aucune requête cross-tenant possible (CLAUDE.md n°9/17).
- **Interdits** (module-rules §2) : tout chemin d'update/delete sur `payment_aggregate_events` (append-only),
  toute purge automatique d'une table d'audit, tout type flottant sur un montant, tout **calcul fiscal**
  (agrégation = pipeline PIP03 ; règle d'imputation/fréquence = paramétrage tenant).
- **Surface publique** : `Contracts/` uniquement (`IPaymentQueries` + DTOs). L'unité de travail d'écriture
  (`IPaymentUnitOfWork`) est **interne** au module (consommée à terme par le pipeline).

## Published Events

Aucun (item TRK04). Le module porte le modèle et la persistance ; les flux d'agrégation/transmission
(événements) arrivent avec le pipeline (PIP03).

## Consumed Events

Aucun (item TRK04). Les paiements bruts sont persistés par le chemin synchrone (push agent → pipeline) ; le
module reste idempotent sur l'identifiant côté écriture (INV-PAYMENTS-004) sans s'abonner lui-même.

## Dependencies

- `Liakont.Agent.Contracts` (`PivotPaymentDto` — porté par le contrat agent, projeté par `PivotPaymentMapper`).
- `Stratum.Common.Infrastructure` (`IConnectionFactory`, `TransactionScope`, `MigrationAssembliesOptions`,
  Dapper/Npgsql/DbUp).
