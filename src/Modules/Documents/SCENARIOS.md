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
- `Issued_Event_Carries_The_Three_Proof_Snapshots` — un document émis archive les 3 snapshots de preuve (payload + réponse PA + trace de mapping) dans son événement (TRK04, INV-011).
- `Rejected_Event_Carries_Payload_And_Pa_Response_But_No_Mapping_Trace` — un document rejeté archive payload + réponse de rejet, sans trace de mapping (la tentative ratée fait partie de l'audit).
- `Issuance_Snapshots_Require_A_Non_Blank_Payload` / `..._Mapping_Trace` / `Rejection_Snapshots_Require_A_Non_Blank_Pa_Response` — snapshots obligatoires (impossible d'enregistrer une preuve vide).
- `MarkIssued_Rejects_Null_Snapshots` — émission sans snapshots refusée, état inchangé.

### DocumentDuplicatePolicyTests (anti-doublon F06 §4 — INV-DOCUMENTS-012)
- `Rule_42_Prior_Issued_By_Functional_Key_Blocks_As_Duplicate` — un Issued de même `(siren, numéro)` bloque (4.2).
- `Rule_43_Prior_RejectedByPa_Allows_Resend_And_Exposes_Document_To_Supersede` — un rejeté autorise le renvoi et désigne l'ancien à superséder (4.3).
- `Rule_44_Same_Payload_Hash_Already_Issued_Blocks_As_Strict_Duplicate` — empreinte identique à un émis = doublon strict (4.4).
- `Rule_45_No_Blocking_Prior_Authorizes_Send` — document inédit : envoi autorisé (4.5).
- `Issued_Takes_Precedence_Over_Rejected_On_The_Same_Functional_Key` / `Functional_Key_Match_Takes_Precedence_Over_The_Hash_Guard` — précédence dans l'ordre de la spec.
- `Non_Issued_Non_Rejected_Priors_Do_Not_Block_Without_A_Hash_Twin` — seuls Issued/RejectedByPa changent le verdict.
- `In_Flight_Prior_Plus_Hash_Twin_Falls_Through_To_The_Strict_Duplicate_Guard` — antécédent en vol + empreinte jumelle = doublon strict.
- `Decide_Rejects_A_Null_Prior_Collection` — garde d'argument.

### DocumentEventSourceAlteredTests (fait d'audit d'altération — INV-DOCUMENTS-013)
- `SourceAlteredAfterIssue_Is_A_System_Audit_Fact_Without_Snapshot` — type, id porté, document émis ciblé, aucun opérateur, sans snapshot, détail nettoyé.
- `SourceAlteredAfterIssue_Requires_A_Detail` — détail obligatoire.

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
- `Issued_Event_Persists_The_Three_Proof_Snapshots_As_Jsonb` — les 3 snapshots d'émission relus sans perte depuis les colonnes jsonb (TRK04, INV-011).
- `Rejected_Event_Persists_Payload_And_Pa_Response_Without_Mapping_Trace` — rejet : payload + réponse archivés, trace de mapping `null`.
- `Operator_Supersede_Persists_Replacement_Link_And_Operator_Identity` — remplacement après rejet : lien remplaçant + identité opérateur relus en base.
- `Transition_Is_Not_Visible_Until_Commit` — read-modify-write SANS commit (dispose → rollback) : ni l'état ni l'événement ne subsistent (atomicité tout-ou-rien).
- `Illegal_Transition_Leaves_Document_And_Audit_Untouched` — transition refusée (exception avant écriture) : document et piste d'audit intacts.
- `GetForUpdate_Returns_Null_When_Document_Absent` — chargement d'un identifiant inconnu = `null`.
- `Concurrent_GetForUpdate_Serializes_Transitions_And_Prevents_Lost_Update` — deux UoW sur le même document : T2 (`GetForUpdateAsync`) reste bloqué tant que T1 n'a pas commit, puis repart de l'état avancé (verrou `FOR UPDATE` : sérialisation, pas de lost-update).
- `Transition_Preserves_All_Non_State_Fields` — un changement d'état (read-modify-write) n'altère ni les montants `decimal` ni `supplier_siren` / `payload_hash` / `first_seen_utc` (garde-fou anti-corruption silencieuse d'un montant audité, CLAUDE.md n°1/4).

### DocumentIntakeIntegrationTests (port d'ingestion PIV04 — INV-DOCUMENTS-003/004/008)
- `Agent_Push_Creates_Detected_Document_With_Genesis_Event` — un push agent crée un document `Detected` + son événement de genèse.
- `Intake_Is_Idempotent_On_DocumentId` — rejeu de l'ingestion : ni document ni audit dupliqués (INV-003).

### TaxReportAndArchiveSchemaIntegrationTests (schéma des tables complémentaires — item TRK01)
- `TaxReport_RoundTrips_All_Columns` — `documents.tax_reports` : colonnes + FK vers `documents` (alimentée par TRK06).
- `ArchiveEntry_RoundTrips_All_Columns` — `documents.archive_entries` : colonnes + FK vers `documents` (alimentée par TRK05).

### AntiDuplicateIntegrationTests (anti-doublon + lookups VAL03/VAL04 + reprise timeout — INV-DOCUMENTS-012/014)
- `Rule_42_Document_Already_Issued_Is_A_Duplicate_And_Is_Blocked` — doublon émis bloqué, document bloquant désigné (F06 §4.2).
- `Rule_43_Resend_After_Rejection_Is_Allowed_And_Names_The_Document_To_Supersede` — renvoi après rejet autorisé, ancien à superséder (F06 §4.3).
- `Rule_44_Same_Payload_Hash_As_An_Issued_Document_Is_A_Strict_Duplicate` — empreinte jumelle déjà émise (autre clé) = doublon strict (F06 §4.4).
- `Rule_45_A_New_Document_Is_Authorized_To_Send` — document inédit autorisé (F06 §4.5).
- `A_Document_Is_Never_A_Duplicate_Of_Itself` — exclusion du candidat de ses propres antécédents.
- `Without_A_Supplier_Siren_The_Functional_Key_Does_Not_Match_A_Prior` — sans SIREN, pas de rapprochement par clé fonctionnelle.
- `IssuedDocumentLookup_Reports_An_Issued_Number_As_Already_Issued` / `..._Does_Not_Report_A_Non_Issued_Or_Unknown_Number` — VAL03 : émis vs non-émis/inconnu.
- `IssuedInvoiceLookup_Maps_Original_Invoice_State` — VAL04 : KnownIssued / KnownNotIssued / Unknown (avoir orphelin, fail-safe).
- `IssuedInvoiceLookup_Discriminates_By_Issue_Date_When_Number_Collides` — numéro homonyme à une autre date = pas la même facture (numéro non unique, F07-F08 §B.2.5 / fail-safe).
- `GetPotentiallySentDocuments_Returns_Only_Sending_Documents` — reprise sur timeout : seuls les documents en cours d'envoi (F06 §5).

### SourceAlterationConsumerIntegrationTests (altération source après émission — INV-DOCUMENTS-013)
- `Alteration_After_Issue_Records_An_Append_Only_Audit_Fact_On_The_Issued_Document` — fait d'audit inscrit sur l'émis, document émis INCHANGÉ (jamais réémis).
- `Consuming_The_Same_Event_Twice_Records_The_Alteration_Only_Once` — consommation idempotente (rejeu d'outbox at-least-once).
- `Alteration_Of_A_Source_Without_An_Issued_Document_Records_Nothing` — pas d'émis pour la référence = rien à inscrire.

### DocumentsModuleRegistrationTests (garde anti faux-vert d'enregistrement)
- `AddDocumentsModule_Enrolls_Module_Assembly_For_Migrations` — l'assembly est enrôlé pour la migration (création des tables `documents.*` en production).
- `AddDocumentsModule_Registers_Persistence_And_Intake_Port` — UoW + requêtes enregistrées, et `IDocumentIntake` branché sur la VRAIE implémentation (pas le no-op).
