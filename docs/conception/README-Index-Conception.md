# Index des documents de conception — Passerelle e-invoicing legacy

> Mis à jour le 2026-06-02. Chaque fonctionnalité majeure a fait l'objet d'une deep research (réglementaire/technique/fonctionnel) puis d'un document de conception actionnable.
> Document parent : `..\Analyse_opportunité\Conception-Produit-Passerelle.md`.

## Documents

| Doc | Fonctionnalités | Recherche | Statut | Solidité de la recherche |
|---|---|---|---|---|
| [F01-F02](F01-F02-Modele-Pivot-Contrat-Extraction.md) | Modèle pivot + contrat d'extraction `IExtractor` | DR1 | 🟨 à revoir | Socle normatif confirmé (AFNOR XP Z12-012/-013/-014, spec DGFiP v3.1) ; détail des BT à dépouiller sur sources primaires |
| [F03](F03-Mapping-TVA.md) | Moteur de mapping TVA paramétrable | DR2 | 🟨 à revoir | ⚠️ codes Peppol/VATEX en abstention (vérificateurs sans accès aux sources) — à re-confirmer ; cohérent avec staging |
| [F04](F04-Controles-Qualite-Validation.md) | Contrôles qualité / validation avant envoi | DR3 | 🟨 à revoir | Référence Schematron CEN/TC 434 + BR-CO-15 confirmés ; détail codes rejet DGFiP à extraire de la norme |
| [F05](F05-Client-API-B2Brouter.md) | Client API B2Brouter (`IPaClient`) | — (validé staging) | 🟨 à revoir | N/A — appuyé sur le RECAP (validé de bout en bout en staging) |
| [F06](F06-Tracking-Piste-Audit.md) | Tracking, anti-doublons, piste d'audit | DR6 | 🟨 à revoir | PAF (art. 289 V/VII), non-altération, conservation 10 ans confirmés ; responsabilités SC/PA = zone d'ombre à border par contrat |
| [F07-F08](F07-F08-Avoirs-Frontiere-B2B-B2C.md) | Avoirs + frontière B2B/B2C (garde-fou pro) | DR4 | 🟨 à revoir | ✅ **la plus solide : 25/25 confirmées sur BOFiP/impots.gouv.fr/OpenPEPPOL** |
| [F09](F09-E-Reporting-Paiement.md) | E-reporting de paiement (Flux 10.2/10.4) | DR5 | 🟨 à revoir | ⚠️ fiches DGFiP en abstention — à re-confirmer ; point ouvert majeur côté B2Brouter |
| [F10](F10-Console-Admin-WPF.md) | Console d'administration WPF | — (interne) | 🟨 à revoir | N/A — conception |
| [F11](F11-CLI-Mode-Automatique.md) | CLI / mode automatique planifié | — (interne) | 🟨 à revoir | N/A — conception (pattern SynchroAxelor) |
| F12 | Configuration / déploiement | — (interne) | ⬜ à faire | reste à rédiger (session de conception) |

## Actions transverses sorties des recherches (à traiter)

| # | Action | Issue de | Priorité |
|---|---|---|---|
| A1 | **Dépouiller les spécifications externes DGFiP v3.1 + normes AFNOR** (liste exacte des BT obligatoires, codes rejet, flux 10.x) | DR1, DR3, DR4 | Haute (phase 2 B2B surtout) |
| A2 | **Ticket B2Brouter : marge (montant cas n°33) + transmission paiements (Flux 10.2/10.4) + calendrier statut « Encaissée »** | DR2, DR5 | **Haute** (bloque F9, conforme la démo) |
| A3 | **Questions fiscales au CMP/expert-comptable** : (a) régime 6 = marge ou hors champ ? (b) option TVA sur les débits ? (c) OperationCategory Mixte ? (d) volume d'acheteurs pros ? | DR2, DR4, DR5 | **Haute** (conditionne F3, F9, F8) |
| A4 | Récupérer les artefacts de validation FNFE-MPE (BR-FR-* / EXTENDED-CTC-FR) | DR3 | Moyenne (phase 2) |
| A5 | Clauses contractuelles (obligation de moyens, qualité donnée source au client, RC Pro, archivage délégué PA) | DR6 | Moyenne (proposition CMP) |
| A6 | Décision RGPD rétention nominatifs acheteurs (DPO CMP) | DR6 | Moyenne |

## Note de méthode sur les recherches

Les 6 deep researches ont produit des résultats de qualité **inégale**, pour une raison technique : l'étape de vérification adversariale a souvent « abstenu » (vote 0-0) quand les agents vérificateurs ne pouvaient pas rouvrir la source citée (PDF impots.gouv.fr, pages docs.peppol.eu). **Une abstention n'est PAS une réfutation** — l'information de la source primaire reste valable, elle n'a simplement pas été re-confirmée par un second passage.

- **DR4 (avoirs/B2B-B2C)** : excellente, 25/25 confirmées. À considérer comme solide.
- **DR1 (pivot)** : socle normatif solide (sources AFNOR/DGFiP primaires confirmées) ; détails fins non confirmés → A1.
- **DR3 (contrôles)** : noyau solide (Schematron, BR-CO-15) ; détails codes rejet → A1.
- **DR6 (audit)** : socle légal solide ; responsabilités = zone d'ombre réelle → A5.
- **DR2 (TVA), DR5 (paiement)** : abstentions massives, MAIS les sources sont primaires (Peppol/EC, fiches DGFiP, décret) et cohérentes avec le staging. À re-confirmer avant figeage (A1, A2, A3) plutôt qu'à refaire.

Aucun résultat n'a produit de contradiction franche avec ce qu'on savait déjà du projet (RECAP, analyse données EncheresV6, staging B2Brouter). Les zones à sécuriser sont des **précisions à obtenir** (sources primaires DGFiP + réponses CMP/B2Brouter), pas des remises en cause.
