# F10 — Console d'administration (WPF)
### Document de conception — Gateway.App

> Statut : 🟨 rédigé sans deep research (conception interne). À revoir ensemble — c'est le composant le plus visible en démo, vos retours comptent le plus ici.
> Dernière mise à jour : 2026-06-02

---

## 1. Objectif et philosophie

Application **WPF desktop**, déployée on-premise sur le serveur (ou un poste d'administration), qui permet à un **opérateur comptable non technique** de :
1. Voir ce qui doit partir, ce qui est parti, ce qui est bloqué
2. Déclencher / re-déclencher des envois
3. Comprendre et corriger les blocages
4. Prouver ce qui a été transmis (piste d'audit consultable)

**Philosophie : simple, lisible, zéro jargon technique.** L'opérateur ne doit jamais voir du JSON ou un code HTTP. Il voit des bordereaux, des états en français et des actions possibles.

C'est aussi le **support de la démo ISATECH** : chaque écran doit être présentable.

## 2. Écrans

### 2.1 Écran principal — « Documents »
La vue centrale. Liste des documents (bordereaux/factures/avoirs) connus de la Passerelle.

```
┌─────────────────────────────────────────────────────────────────────────┐
│  [Période: 01/06 ▾ → 30/06 ▾]  [État: Tous ▾]  [Type: Tous ▾]  [🔍....] │
│                                                          [⟳ Actualiser] │
├──────┬──────────┬────────────┬──────────┬──────────┬─────────┬─────────┤
│ N°   │ Date     │ Acheteur   │ Montant  │ Type     │ État    │ Actions │
├──────┼──────────┼────────────┼──────────┼──────────┼─────────┼─────────┤
│ 2018 │ 01/06/26 │ DUPONT J.  │ 1 162,80 │ Bordereau│ ✅ Émis │ [Voir]  │
│ 2019 │ 01/06/26 │ MARTIN SAS │ 3 410,00 │ Bordereau│ 🛑 Bloqué│ [Voir]  │
│ 2020 │ 01/06/26 │ BERNARD P. │   540,00 │ Avoir    │ ⏳ À envoyer│[Voir]│
│ 2021 │ 02/06/26 │ PETIT M.   │ 2 100,00 │ Bordereau│ ❌ Rejeté│ [Voir]  │
├──────┴──────────┴────────────┴──────────┴──────────┴─────────┴─────────┤
│  4 documents — 1 émis · 1 à envoyer · 1 bloqué · 1 rejeté               │
│                              [▶ Envoyer la sélection]  [▶▶ Tout envoyer]│
└─────────────────────────────────────────────────────────────────────────┘
```

**Filtres** : période (par défaut : mois courant), état, type (bordereau/avoir), texte libre (n°, nom acheteur).
**Compteurs** : bandeau de synthèse permanent (combien dans chaque état).

### 2.2 États affichés (vocabulaire opérateur)

| État interne (Tracking) | Affiché | Couleur | Signification opérateur |
|---|---|---|---|
| `Detected` | ⏳ À envoyer | gris | Lu dans le logiciel source, prêt |
| `Blocked` | 🛑 Bloqué | orange | Un contrôle a échoué AVANT envoi (SIREN invalide, régime non mappé, acheteur pro…) — action requise |
| `Sending` | ⏫ En cours | bleu | Envoi en cours |
| `Issued` | ✅ Émis | vert | Accepté par la PA, tax report généré |
| `RejectedByPa` | ❌ Rejeté | rouge | La PA a refusé — voir le motif, corriger, renvoyer |
| `TechnicalError` | ⚠️ Erreur technique | rouge | Problème réseau/API — sera retenté automatiquement |
| `Superseded` | ↪ Remplacé | gris clair | Renvoyé sous un autre numéro après rejet |

### 2.3 Écran détail document
Ouvert par [Voir]. Trois onglets :

1. **Contenu** — le document tel que lu : en-tête (n°, date, acheteur, totaux) + lignes (libellé, montants, régime TVA appliqué → catégorie/VATEX résultante). Mise en évidence de ce qui a déclenché un blocage le cas échéant.
2. **Contrôles** — la liste des contrôles passés/échoués (cf. F4) : ✅/🛑/⚠️ par contrôle, avec le message explicatif et, si possible, l'action corrective ("Le SIREN de l'émetteur est invalide → vérifier la configuration", "Acheteur professionnel détecté → ce document doit être traité manuellement").
3. **Historique** — chronologie complète : détecté le, envoyé le, réponse PA, tax report, états successifs. C'est la vue "piste d'audit". Bouton [Exporter] (→ PDF/texte, pour un contrôle).

Actions contextuelles selon l'état : [Envoyer], [Renvoyer], [Marquer comme traité manuellement], [Ignorer définitivement] (avec motif obligatoire, journalisé).

### 2.4 Écran « Encaissements » (si F9 retenu en V1)
Même logique que Documents, mais pour les données de paiement : période, montants encaissés par jour/taux, état de transmission du flux 10.4.

### 2.5 Écran « Configuration » (lecture seule en V1)
Affiche la configuration active SANS permettre de la modifier (la modification = éditer le fichier de config + redémarrer, assumé en V1) :
- Identité émetteur (SIREN, raison sociale)
- Connexion source (chaîne masquée, état : ✅ accessible / ❌ inaccessible)
- Compte B2Brouter (account id, environnement staging/prod, consommation transactions : X / limite)
- **Table de mapping TVA** (régime → catégorie/VATEX) — l'afficher est important : c'est ce que l'expert-comptable doit valider
- Planification (heure du run automatique)

> Décision à valider : faut-il permettre l'édition de la table TVA depuis la console (plus confortable, plus risqué) ou la garder en fichier (plus simple, plus auditable) ? Recommandation V1 : **fichier, console en lecture seule**.

### 2.6 Écran « Journal »
Liste des runs (manuels et planifiés) : date, durée, documents traités/envoyés/bloqués/rejetés, lien vers le log détaillé. Permet de répondre à "est-ce que ça a tourné cette nuit ?" en un coup d'œil.

## 3. Architecture technique

- **MVVM minimal sans framework** (pas de Prism/MVVMLight — overkill) : `INotifyPropertyChanged` + `RelayCommand` maison (~50 lignes).
- La console **n'appelle jamais l'API B2Brouter directement** : elle utilise les mêmes services du Core que le CLI (`Pipeline`, `Tracking`, `IPaClient`). Console = une vue sur le Tracking + un déclencheur de Pipeline.
- Lecture de la même base SQLite que le CLI → **verrou applicatif** : si un run CLI est en cours, la console l'affiche et désactive les boutons d'envoi (cf. F11, mutex nommé système).
- Rafraîchissement : bouton manuel + timer optionnel (60 s). Pas de push.
- Thème : sobre, professionnel. Pas de dépendance à des packs de thèmes payants. (MahApps.Metro est une option gratuite à considérer pour un rendu démo plus moderne — décision esthétique à prendre avant la démo ISATECH.)

## 4. Ce que la console ne fait PAS (V1)

- Pas d'édition de la configuration (sauf décision contraire)
- Pas de création/modification de documents (la source de vérité = le logiciel legacy)
- Pas de gestion des utilisateurs/droits (l'accès au poste = le droit)
- Pas de visualisation des XML Factur-X/UBL (le `xml_base64` des tax reports est archivé par le Tracking, exportable depuis l'historique, pas affiché)

## 5. Décisions à valider ensemble

| # | Décision | Options | Recommandation |
|---|---|---|---|
| 1 | Édition de la table TVA dans la console | oui / lecture seule | lecture seule en V1 |
| 2 | Thème visuel | WPF natif sobre / MahApps.Metro | MahApps pour la démo (gratuit, moderne) |
| 3 | Écran Encaissements en V1 | oui / différé avec F9 | suit la décision F9/DR5 |
| 4 | Langue | français uniquement / FR+EN | français uniquement (cible 100 % FR) |
| 5 | L'action « Tout envoyer » demande-t-elle confirmation avec récapitulatif ? | oui / non | oui (montant total + nombre, irréversible) |
