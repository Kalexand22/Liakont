# Spécifications externes DGFiP v3.2 — Note de lecture Conformat

**Source officielle** : https://www.impots.gouv.fr/specifications-externes-b2b
**Version** : 3.2, publiée le 30/04/2026 (page modifiée le 07/05/2026)
**Téléchargé le** : 2026-06-02
**Versions antérieures** : la v3.1 (31/10/2025) était la référence citée par docs/conception/F01-F02.

## Verdict pour Conformat : delta v3.1 → v3.2 MINIME

Le changelog officiel (`3- XSD_v3.2/Changelog_XSD.md`) liste les changements v3.1 → v3.2 :

| Domaine | Changement v3.1 → v3.2 | Impact Conformat |
|---|---|---|
| **E-reporting** (Flux 10.x — le cœur de Conformat V1) | Suppression de l'attribut `xmlns:ereporting` dans ereporting.xsd | **Aucun** (cosmétique XML ; de plus, Conformat ne produit pas ce XML — les PA le font) |
| E-invoicing (Flux 1/2 — phase 2 Conformat) | Mise en commentaire de BG-25 (lignes de facture) dans le profil CII BASE | Aucun en V1 (B2B = phase 2) ; à re-regarder en phase 2 |
| Annuaire (Flux 11-12) | Aucun changement | Aucun |

**Conclusion** : les specs internes F01-F11 (rédigées sur la base v3.1) restent valides.
Aucune modification du backlog n'est requise par la v3.2.

## Rappel d'architecture : pourquoi l'impact est structurellement faible

Conformat ne dialogue **jamais directement** avec le PPF/DGFiP : il transmet des données
structurées aux **Plateformes Agréées** (B2Brouter, Super PDP...) via leurs API propres,
et ce sont les PA qui produisent les flux DGFiP conformes (Factur-X, Flux 10.x XML).
Les XSD DGFiP servent à Conformat de **référence de validation amont** (s'assurer que les
données transmises permettront à la PA de produire un flux valide), pas de format de sortie.

## Contenu du paquet officiel (ce dossier)

| Fichier/Dossier | Contenu | Pertinence Conformat |
|---|---|---|
| `0- Dossier de specifications externes FE - Dossier général_v3.2.pdf` | LA spec complète (flux, acteurs, cas d'usage, cycle de vie) | ★★★ Référence principale |
| `1- Dossier ... Chorus Pro_v1.1.pdf` | Spécifique B2G (facturation au secteur public) | ★ Hors périmètre V1 |
| `2- Annexes_v3.2/Annexe 1 - Format sémantique e-invoicing Flux 1 v1.2.xlsx` | BT/BG du Flux 1 | ★★ Phase 2 (B2B) |
| `2- Annexes_v3.2/Annexe 2 - Format sémantique CDV Flux 6 V2.3.xlsx` | Statuts de cycle de vie | ★★ Suivi des statuts (TRK) |
| `2- Annexes_v3.2/Annexe 3 - Format sémantique annuaire V1.8.xlsx` | Annuaire | ★ Phase 2 |
| `2- Annexes_v3.2/Annexe 6 - Format sémantique e-reporting V1.10.xlsx` | **Flux 10.x : TT/TG de l'e-reporting** | ★★★ Cœur V1 (PIV02, PIP03, PAA01) |
| `2- Annexes_v3.2/Annexe 7 - Règles de gestion V1.9.xlsx` | **Règles de validation officielles** | ★★★ Lot VAL (à croiser avec F04) |
| `3- XSD_v3.2/1 - E-reporting/` | payment.xsd, report.xsd, transaction.xsd, ereporting.xsd | ★★★ Structure officielle des Flux 10.x |
| `3- XSD_v3.2/2 - E-invoicing/` | CII D22B + UBL 2.1 (profils BASE et FULL) | ★★ Phase 2 + validation Factur-X récupéré |
| `3- XSD_v3.2/0 - Annuaire/` | XSD annuaire | ★ Phase 2 |
| `3- XSD_v3.2/Changelog_XSD.md` | Changelog officiel v3.0→v3.1→v3.2 | ★★★ Traçabilité |
| `4- Swagger_v3.2/ppf-openapi-annuaire-*.json` | OpenAPI annuaire PPF | ★ Phase 2 |

## Actions restantes (humaines)

- [ ] Lecture du Dossier général v3.2 (PDF, ~3 MB) pour vérifier les évolutions de TEXTE
      (le changelog ne couvre que les XSD — les règles de gestion de l'Annexe 7 V1.9 et le
      dossier général peuvent contenir des précisions nouvelles vs v3.1)
- [ ] Croiser l'Annexe 7 (règles de gestion V1.9) avec F04 lors du lot VAL
- [ ] Télécharger les normes AFNOR XP Z12-012/-013/-014 (payantes, boutique AFNOR — non incluses ici)
