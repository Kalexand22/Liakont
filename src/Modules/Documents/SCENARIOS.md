# Scénarios de test — module Documents

## Unit (`Tests.Unit`)

### DocumentTests (agrégat Document + genèse — INV-DOCUMENTS-001/004/005)
- `CreateDetected_Sets_State_Detected_And_Timestamps` — état initial `Detected`, `first_seen` = `last_update`, PA/mapping nuls (INV-004).
- `CreateDetected_Preserves_Decimal_Amounts_Exactly` — montants `decimal` conservés (INV-001).
- `CreateDetected_Requires_SourceReference` / `..._DocumentNumber` / `..._DocumentType` / `..._PayloadHash` — champs obligatoires rejetés à blanc.
- `CreateDetected_Trims_Mandatory_Strings` — espaces de bord supprimés.
- `CreateDetected_Blank_Siren_And_Customer_Become_Null` — champ source absent = `null` (INV-005).
- `DocumentEvent_Detected_Is_System_Genesis` — événement de genèse système (type `DocumentDetected`, aucun opérateur, sans snapshot).

### DetectedDocumentMapperTests (projection pivot → Document — INV-DOCUMENTS-002/003)
- `Maps_Core_Fields_From_Pivot_And_Intake` — identité, numéro, type BRUT, date, SIREN, état `Detected`.
- `Preserves_Tricky_Decimal_Amounts` — 1000.00 / 162.80 / 1162.80 reportés sans calcul (INV-001/002).
- `Maps_Company_Hint_When_Customer_Is_Company` — indice « société » reporté tel quel (aucune heuristique).
- `Maps_Null_Customer_To_Null_Name_And_False_Hint` — B2C sans tiers : destinataire `null` (INV-005).
- `Maps_Null_Supplier_Siren` — SIREN fournisseur absent = `null` (INV-005).

### DocumentStateMachineTests (table des transitions — INV-DOCUMENTS-009)
- `Allows_Exactly_The_Specified_Transitions_And_No_Other` — balaie toutes les paires (from, to) : la table autorise EXACTEMENT les 11 transitions de la spec (F06 §3), ni plus, ni moins.
- `States_Without_Successor_Have_No_Outgoing_Transition` — `Issued` / `Superseded` / `ManuallyHandled` n'ont aucune sortie.
- `EnsureCanTransition_Throws_On_An_Illegal_Transition` / `..._Does_Not_Throw_On_A_Legal_Transition` — contrôle de légalité (exception ciblée `from`/`to`).

### DocumentTransitionTests (machine à états de l'agrégat — INV-DOCUMENTS-009/010)
- `Nominal_Cycle_Detected_To_Issued_Writes_One_Event_Per_Transition` — Detected→ReadyToSend→Sending→Issued ; chaque transition produit son événement typé et avance `LastUpdateUtc`.
- `Detected_Can_Be_Blocked_With_Reason_Then_Made_ReadyToSend` — blocage avec motif puis remise en file.
- `Illegal_Transition_Is_Rejected_And_Leaves_State_Unchanged` — transitions interdites rejetées, état inchangé (contrôle avant mutation).
- `TechnicalError_Can_Be_Retried_To_ReadyToSend` — reprise re-tentable après erreur technique.
- `Blocked_To_ManuallyHandled_Records_Reason_And_Operator_Identity` / `RejectedByPa_Can_Also_Be_Manually_Handled` — traitement manuel (terminal) avec motif + opérateur.
- `ManuallyHandled_Requires_A_Reason` / `..._An_Operator_Identity` — motif et identité opérateur obligatoires.
- `RejectedByPa_To_Superseded_Records_Replacement_Reference_And_Operator` — remplacement après rejet (terminal), lien remplaçant + opérateur journalisés.
- `Supersede_Requires_A_Replacement_Reference` — référence du remplaçant obligatoire.
- `States_Without_Successor_Reject_Every_Transition` — toute tentative de sortie d'un état sans suite (Issued/terminaux) échoue, même avec des arguments valides.

## Integration (`Tests.Integration`, Testcontainers PostgreSQL)

### DocumentPersistenceIntegrationTests (repository + lectures — INV-DOCUMENTS-001/003/006)
- `CreateDetected_RoundTrips_All_Fields_And_Writes_Genesis_Event` — tous les champs relus à l'identique + 1 événement de genèse.
- `CreateDetected_RoundTrips_Decimal_Amounts_Without_Loss` — round-trip decimal sans perte sur des montants piégeux (INV-001).
- `GetById_Returns_Null_When_Absent` — document inexistant = `null`.
- `CreateDetected_Is_Idempotent_On_Id` — second push du même identifiant : aucune recréation, audit non dupliqué (INV-003).
- `Upsert_Updates_Existing_By_Id_And_Preserves_FirstSeen` — upsert par identifiant met à jour l'état/PA/mapping et préserve `first_seen_utc`.
- `GetByNumber_Returns_Most_Recent_For_That_Number` — numéro non unique : le plus récent est retourné (INV-006).
- `GetByState_Is_Paginated_And_Filtered` — file par état paginée, triée par dernière mise à jour décroissante.

### DocumentEventAppendOnlyIntegrationTests (immuabilité de l'audit — INV-DOCUMENTS-007)
- `Update_Of_An_Event_Is_Rejected` — UPDATE rejeté par trigger (message « append-only »), entrée intacte.
- `Delete_Of_An_Event_Is_Rejected` — DELETE rejeté par trigger, entrée intacte.
- `Truncate_Of_The_Audit_Table_Is_Rejected` — TRUNCATE de masse rejeté par trigger d'instruction.

### DocumentStateTransitionIntegrationTests (transitions persistées atomiquement — INV-DOCUMENTS-009/010)
- `Transition_Persists_New_State_And_Audit_Event_Atomically` — `GetForUpdateAsync` (verrou) → transition → upsert état + append événement dans la même transaction ; relu : nouvel état + événement typé.
- `Full_Nominal_Cycle_Is_Persisted_With_One_Event_Per_Transition` — Detected→…→Issued sur plusieurs transactions ; 4 événements dans l'ordre chronologique.
- `Operator_Supersede_Persists_Replacement_Link_And_Operator_Identity` — remplacement après rejet : lien remplaçant + identité opérateur relus en base.
- `Transition_Is_Not_Visible_Until_Commit` — read-modify-write SANS commit (dispose → rollback) : ni l'état ni l'événement ne subsistent (atomicité tout-ou-rien).
- `Illegal_Transition_Leaves_Document_And_Audit_Untouched` — transition refusée (exception avant écriture) : document et piste d'audit intacts.
- `GetForUpdate_Returns_Null_When_Document_Absent` — chargement d'un identifiant inconnu = `null`.

### DocumentIntakeIntegrationTests (port d'ingestion PIV04 — INV-DOCUMENTS-003/004/008)
- `Agent_Push_Creates_Detected_Document_With_Genesis_Event` — un push agent crée un document `Detected` + son événement de genèse.
- `Intake_Is_Idempotent_On_DocumentId` — rejeu de l'ingestion : ni document ni audit dupliqués (INV-003).

### TaxReportAndArchiveSchemaIntegrationTests (schéma des tables complémentaires — item TRK01)
- `TaxReport_RoundTrips_All_Columns` — `documents.tax_reports` : colonnes + FK vers `documents` (alimentée par TRK06).
- `ArchiveEntry_RoundTrips_All_Columns` — `documents.archive_entries` : colonnes + FK vers `documents` (alimentée par TRK05).

### DocumentsModuleRegistrationTests (garde anti faux-vert d'enregistrement)
- `AddDocumentsModule_Enrolls_Module_Assembly_For_Migrations` — l'assembly est enrôlé pour la migration (création des tables `documents.*` en production).
- `AddDocumentsModule_Registers_Persistence_And_Intake_Port` — UoW + requêtes enregistrées, et `IDocumentIntake` branché sur la VRAIE implémentation (pas le no-op).
