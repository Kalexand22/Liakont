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

### Commercial (hors backlog technique)
- [ ] Relancer ISATECH (dossier CMP + période d'observation RJ jusqu'au 7 juillet 2026)
- [ ] Télécharger spécifications externes DGFiP v3.1 + normes AFNOR XP Z12-012/-013/-014
