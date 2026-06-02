# Plan de travail courant

_Ce fichier est le plan de la session interactive en cours. En mode orchestration,
le backlog autoritaire est `orchestration/manifest.yaml`._

## Prochaine étape

- [ ] Lancer une session d'orchestration : `Lis orchestration/prompt.md et exécute-le.`
      → L'agent prendra SOL01 (création de la solution Gateway.sln, structure blueprint.md §4).

## Actions humaines à mener en parallèle du développement (hors orchestration)

Ces points appartiennent à des humains. Grâce à l'architecture générique (plug-ins + paramétrage),
ils ne bloquent PAS le développement du produit — ils bloquent les gates de déploiement concernées.

### Bloquant pour GATE_PROD_CMP (production CMP)
- [ ] Expert-comptable CMP : régime 6 = marge EU-J ou hors champ ? → consigner dans deployments/cmp/DECISIONS-FISCALES.md
- [ ] Expert-comptable CMP : TVA sur les débits optée ? (conditionne l'e-reporting paiement pour CE déploiement)
- [ ] Expert-comptable CMP : OperationCategory = Mixte ?
- [ ] Expert-comptable CMP : volume d'acheteurs professionnels ?
- [ ] Ticket support B2Brouter : montant marge cas n°33
- [ ] Ticket support B2Brouter : transmission Flux 10.2/10.4 (calendrier) → mettra à jour les capacités du plug-in
- [ ] Ticket/vérification staging B2Brouter : endpoint de téléchargement de la facture Factur-X générée (→ capacité SupportsDocumentRetrieval, archivage des factures légales)
- [ ] Compte B2Brouter production + tax_report_setting

### Bloquant pour GATE_PA_SUPERPDP (plug-in Super PDP)
- [ ] Ouvrir une sandbox Super PDP (action DR17-A4, ~1-2 jours)
- [ ] Questions support Super PDP : flux paiement 10.2/10.4, archivage NF Z42-013, sort des archives en cas de résiliation

### Veille réglementaire (détectée par la review indépendante — bloquant pour les lots PIV/PAA/PAB)
- [ ] **Télécharger la DERNIÈRE version des spécifications externes DGFiP** sur
      https://www.impots.gouv.fr/specifications-externes-b2b — la page a été modifiée le 07/05/2026,
      une version postérieure à la v3.1 (31/10/2025) citée dans F01-F02 existe probablement (v3.2 ?).
      Dépouiller les écarts (BT, flux 10.x, XSD, swaggers, règles) et mettre à jour F01-F02.
- [ ] Télécharger les normes AFNOR XP Z12-012/-013/-014
- [ ] Vérifier l'impact de la recodification des textes fiscaux applicable au 1er septembre 2026
      sur les références juridiques des specs (art. 289 CGI, etc.)

### Questions techniques ouvertes (à trancher par ADR pendant les items concernés)
- [ ] TRK07 : bibliothèque/implémentation OpenTimestamps en net48 (spike requis — pas un simple POST)
- [ ] TRK07 : RFC 3161 en net48 (les API .NET 5+ n'existent pas — ASN.1 manuel, BouncyCastle, ou report V1.1)
- [ ] TRK08 : moyen d'extraction de texte PDF en net48 — attention licence (iTextSharp = AGPL interdit ;
      candidats : PdfPig Apache-2.0, Docnet.Core) — ou retirer la stratégie 2 du matching V1
- [ ] WPF08 : aperçu PDF dans la console (recommandation V1 : ouverture dans le lecteur du poste)
- [ ] API01 : prérequis réseau Windows (urlacl, SPN, firewall, postes hors domaine)

### Commercial (hors backlog technique)
- [ ] Relancer ISATECH (dossier CMP + période d'observation RJ jusqu'au 7 juillet 2026)
- [ ] Décliner la correction de l'offre (périmètre V1 : pas de réception native, pas de Flux 10.1,
      génération Factur-X par la PA) dans tout support commercial déjà diffusé
