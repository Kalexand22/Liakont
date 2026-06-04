# Scénarios de test — module Payments

## Unit (`Tests.Unit`)

### PaymentTests (encaissement brut — INV-PAYMENTS-001/002/003)
- `Create_Keeps_Source_Amount_And_Fields` — montant et champs reportés tels quels.
- `Create_Keeps_Decimal_Amount_Exact_Without_Float_Drift` — 0.1 + 0.2 = 0.3 exact en decimal (INV-001).
- `Create_Allows_Negative_Amount_For_Refund` — montant négatif autorisé (remboursement — F09 §5.4, INV-003).
- `Create_Rejects_Amount_With_More_Than_2_Decimals` — garde-fou d'intégrité de stockage (INV-001).
- `Create_Accepts_Amount_With_Two_Or_Fewer_Decimals` — 2 décimales ou moins acceptées.
- `Create_Blank_Optional_Fields_Become_Null` — champ optionnel vide = `null`.

### PaymentAggregateTests (agrégat jour × taux + machine à états — INV-PAYMENTS-001/003/007)
- `Create_Starts_In_Calculated_With_Source_Values` — état initial `Calculated`, valeurs reportées.
- `Create_Allows_Negative_Amounts_For_Rectification` — base/TVA négatives autorisées (F09 §5.4, INV-003).
- `Create_Requires_A_Period` — période déclarative obligatoire.
- `Create_Rejects_Taxable_Base_With_More_Than_2_Decimals` / `..._Vat_Rate_With_More_Than_4_Decimals` — gardes de stockage (INV-001).
- `Nominal_Transmission_Cycle_Calculated_To_Transmitted` — Calculated→Sending→Transmitted ; Sending sans snapshot, Transmitted avec snapshots.
- `Rejected_Transmission_Carries_Snapshots` — rejet : payload + réponse PA archivés, motif journalisé.
- `TechnicalError_Can_Be_Retried_To_Sending` — reprise re-tentable après erreur technique.
- `Illegal_Transition_Is_Rejected_And_Leaves_State_Unchanged` — transitions interdites rejetées, état inchangé (INV-007).
- `Terminal_States_Reject_Every_Transition` — `Transmitted` / `RejectedByPa` n'ont aucune sortie.
- `MarkTransmitted_Rejects_Null_Snapshots` — transmission sans snapshots refusée, état inchangé.

### PaymentAggregateEventTests (entrée d'audit — INV-PAYMENTS-005/007)
- `Genesis_Is_Calculated_Without_Snapshots` — genèse en état `Calculated`, sans snapshot.
- `Transition_Carries_State_And_Optional_Snapshots` — l'événement porte l'état atteint et ses snapshots.

### PivotPaymentMapperTests (projection pivot → Payment — INV-PAYMENTS-002)
- `ToPayment_Reports_Source_Values_And_Reduces_Date_To_Day` — report fidèle, date ramenée au jour (F09 §2).
- `ToPayment_Keeps_Optional_Fields_Null_When_Absent` — champs optionnels absents = `null`.

## Integration (`Tests.Integration`, Testcontainers PostgreSQL)

### PaymentPersistenceIntegrationTests (round-trip + idempotence — INV-PAYMENTS-001/003/004)
- `Payment_Round_Trips_Decimal_Amount_Without_Loss` — montants piégeux (0.30 / 1162.80 / 0.01 / -50.00) relus sans perte (INV-001).
- `SavePayment_Is_Idempotent_On_Id` — re-push du même paiement : aucun doublon (INV-004).
- `Aggregate_Round_Trips_Amounts_And_Rate_Without_Loss` — base/TVA négatives + taux 5,5 % relus exactement ; genèse écrite atomiquement.
- `CreateAggregate_Is_Idempotent_On_Id` — agrégat déjà créé : ni réinsertion ni genèse dupliquée (INV-004).

### PaymentAggregateTransmissionIntegrationTests (transitions persistées atomiquement — INV-PAYMENTS-007)
- `Transmission_Persists_State_And_Proof_Snapshots_As_Jsonb` — Calculated→Sending→Transmitted ; 3 événements ordonnés, snapshots relus depuis jsonb, Sending sans snapshot.
- `Transition_Is_Not_Visible_Until_Commit` — read-modify-write SANS commit (dispose → rollback) : seule la genèse subsiste (atomicité).
- `Illegal_Transition_Leaves_Aggregate_And_Audit_Untouched` — transition refusée (exception avant écriture) : agrégat et audit intacts.

### PaymentAggregateEventAppendOnlyIntegrationTests (immuabilité de l'audit — INV-PAYMENTS-005)
- `Update_Of_An_Event_Is_Rejected` — UPDATE rejeté par trigger (message « append-only »), entrée intacte.
- `Delete_Of_An_Event_Is_Rejected` — DELETE rejeté par trigger, entrée intacte.
- `Truncate_Of_The_Audit_Table_Is_Rejected` — TRUNCATE de masse rejeté par trigger d'instruction.

### PaymentsModuleRegistrationTests (garde anti faux-vert d'enregistrement)
- `AddPaymentsModule_Enrolls_Module_Assembly_For_Migrations` — l'assembly est enrôlé pour la migration (création des tables `payments.*` en production).
- `AddPaymentsModule_Registers_Persistence` — UoW + requêtes enregistrées.
