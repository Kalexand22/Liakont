# Lessons Learned

_Ce fichier est mis à jour après chaque correction utilisateur ou erreur d'orchestration.
Il sert de mémoire pour éviter de répéter les mêmes erreurs. Les agents le lisent au début
de chaque session (protocol.md, Step 1)._

---

## 2026-06-02 — Leçon majeure : Liakont est un PRODUIT, pas un projet client

**Symptôme :** Le backlog v1 contenait la table de mapping TVA du CMP comme item du Core
(TVA04), un flag produit `PaymentReportingEnabled` calé sur les limites de B2Brouter, et
visait « la démo ISATECH » comme objectif final. L'utilisateur a corrigé fermement
(« On code un produit GÉNÉRIQUE, pas un spé. Doit être du paramétrage, point final »).

**Règles pour l'avenir :**
- **Toute donnée d'UN client** (table TVA, SIREN, chaîne ODBC, compte PA) va dans
  `deployments/<client>/`, jamais dans `src/` ni `config/exemples/`. Les exemples du Core
  utilisent des codes FICTIFS.
- **Toute limite d'UN PA** s'exprime par les capacités déclarées de SON plug-in
  (`PaCapabilities`), jamais par un flag produit, jamais par un `if` sur le type de PA.
- **Avant de créer un item de backlog**, se demander : « est-ce du PRODUIT (générique,
  réutilisable pour tout client/PA) ou du PARAMÉTRAGE (spécifique à un déploiement) ? »
  Les deux ne vont pas dans le même segment.
- Quand un document commercial (DR9, DR17, Offre-Editeur) mentionne un acteur (Super PDP),
  le chercher dans TOUTE la doc avant de dire qu'il n'y est pas.

## 2026-06-02 — Orchestration : collision de namespace de refs git

**Symptôme :** Première session d'orchestration bloquée sur SOL01 — impossible de créer
`feat/socle/SOL01` car la branche `feat/socle` existe (git stocke les refs comme fichiers :
un chemin ne peut pas être à la fois fichier et répertoire).

**Règle :** Les sous-branches utilisent un TIRET : `<segment>-<item>` (ex: `feat/socle-SOL01`).
Jamais de slash après un nom de branche existante. Corrigé dans protocol.md Step 3.

## 2026-06-02 — Leçons héritées de Stratum (applicables ici)

- **Ne jamais enchaîner un 2ème item après le premier** : un item par session, EXIT propre.
- **Les tests doivent être EXÉCUTÉS, pas seulement écrits** : un test jamais lancé est un faux vert.
- **La review doit porter sur l'arbre de travail courant** : toute modification après review
  invalide la review.
- **Les P2 ne sont jamais ignorés silencieusement** : fixés ou acceptés avec justification écrite.

## 2026-06-02 — Concevoir le backlog en déroulant la journée de l'OPÉRATEUR

**Symptôme :** L'utilisateur a détecté deux trous dans le backlog v3 :
1. Aucune UI de paramétrage comptable — l'écran Configuration était en lecture seule alors que
   le flux nominal du produit (régime TVA non mappé → documents bloqués) exige que le COMPTABLE
   complète la table lui-même, sans intervention IT sur des fichiers serveur.
2. L'accès aux PDF supposait toujours un lien explicite document ↔ fichier, alors que beaucoup
   de logiciels legacy déposent tous les PDF dans un même dossier sans lien fiable
   (→ besoin d'un moteur de réconciliation).

**Règles pour l'avenir :**
- Pour chaque blocage que le produit peut produire, se demander : « QUI le résout, et le backlog
  lui donne-t-il un écran/outil pour le faire ? » Un blocage sans chemin de résolution opérateur
  est un trou fonctionnel.
- Pour chaque donnée attendue d'un système legacy, prévoir le cas dégradé : la donnée existe
  mais SANS lien exploitable (fichiers en vrac, champs libres). La capacité « propre » et la
  capacité « dégradée + réconciliation » sont deux capacités distinctes.

## 2026-06-02 — Leçons de la review indépendante (backlog v4 → v5)

Une review indépendante (47 findings, 46 confirmés) a été menée avant le lancement de
l'orchestration. Les patterns d'erreur qu'elle révèle :

1. **Amender les specs le jour où une décision les rend obsolètes.** F10/F11 décrivaient encore
   l'architecture pré-API : les items étaient corrects mais les specs les contredisaient — un agent
   qui lit la spec en premier aurait codé la mauvaise architecture. Règle : toute décision qui
   contredit une spec = note d'amendement datée EN TÊTE de la spec, le jour même de la décision.
2. **Tester les faux verts de l'outillage AVANT de s'y fier.** codex-review.ps1 retournait 0 dès
   que le reviewer répondait, même avec des P1 — toute la boucle de review reposait sur un faux vert.
   Règle : pour chaque script de vérification, exécuter le scénario « ça DOIT échouer » avant
   de l'utiliser (c'est déjà la règle SOL02 pour verify-fast : la généraliser à tout l'outillage).
3. **Câbler les consommateurs, pas seulement les producteurs.** TRK03 exposait
   GetPotentiallySentDocuments, PAA01 exposait GetGeneratedDocumentAsync — mais PIP01 (le
   consommateur) ne les appelait pas : capacités produites jamais consommées = trous fonctionnels.
   Règle : chaque méthode/capacité introduite par un item doit avoir son consommateur identifié
   dans la description d'un item (le même ou un autre, nommé explicitement).
4. **Vérifier l'ordonnancement par les CONSOMMATEURS.** CFG02 (GatewayConfig) était ordonné après
   la console alors que le Service/API/CLI en dépendent. Règle : pour chaque item, se demander
   « qui consomme ce que je produis, et est-il APRÈS moi dans le graphe ? »
5. **Une review indépendante avant tout lancement d'orchestration.** Coût : 1 session.
   Gain ici : 28 P1 dont des faux verts d'outillage et des inversions de dépendances qui
   auraient bloqué les agents en plein segment.

## 2026-06-02 — Spécifique Liakont (conformité fiscale)

- **Ne jamais inventer une règle fiscale.** Si la spec (docs/conception/) ne tranche pas une
  question TVA/VATEX/arrondi, l'item passe en `blocked` avec le nom de la décision manquante
  et son propriétaire (expert-comptable du client ou support PA). Deviner = risque fiscal client.
- **Les montants sont en decimal.** Tout float/double sur un montant est un P1, sans exception.
- **La piste d'audit et le coffre d'archive sont sacrés.** Aucun code ne doit pouvoir modifier
  ou supprimer un DocumentEvent ou un paquet d'archive, même pour « corriger » des données de test.
- **PowerShell 5.1 + fichiers UTF-8 sans BOM = corruption d'accents.** Les .ps1 doivent avoir
  un BOM UTF-8 ; les lectures/écritures de fichiers en .NET doivent spécifier l'encodage UTF-8
  explicitement.

## 2026-06-03 — Leçons du traitement des reviews v6/v8 (backlog v6 → manifest v7)

1. **L'outil Write (réécriture complète) de Claude Code produit de l'UTF-8 SANS BOM.** Réécrire
   un .ps1 avec Write casse son parsing par PowerShell 5.1 dès qu'il contient un caractère
   non-ASCII (tiret cadratin, accents) — erreur détectée par l'exécution du script juste après.
   Règle : après tout Write d'un .ps1, restaurer le BOM immédiatement
   (`[System.IO.File]::WriteAllText($f, $contenu, [System.Text.UTF8Encoding]::new($true))`).
   L'outil Edit (remplacement partiel) préserve l'encodage existant — le préférer pour les .ps1.
2. **Vérifier les hypothèses sur le socle vendored SUR PIÈCE, pas sur sa réputation.** Trois
   briques Stratum supposées « directes » ne l'étaient pas : Identity référence Party.Contracts
   en dur, le transport email est un stub, le module Job n'a aucune résolution de tenant.
   Règle : pour chaque capacité du socle qu'un item consomme, citer le fichier/la ligne du socle
   qui la fournit — « le module X le fait » sans référence = hypothèse à vérifier.
3. **Toute feature distribuée a DEUX côtés — vérifier que le backlog couvre les deux.**
   L'auto-update avait son côté agent (AGT04) et son contrat (PIV05) mais AUCUN côté plateforme
   (registre, publication, politique de flotte) : la flotte n'aurait jamais pu se mettre à jour.
   Règle : pour chaque flux client↔serveur, lister les items des deux côtés et leurs consommateurs.
4. **Un point d'entrée qui contredit le protocole est une corruption en attente.** prompt.md
   disait « create state.yaml if absent », protocol.md disait « never recreate it » : un agent
   mal configuré aurait ressuscité des items terminés. Règle : les invariants critiques
   (état, verrous) ne sont énoncés qu'à UN endroit ; les autres documents y renvoient.
5. **Les gardes anti-faux-vert se testent dans les deux sens.** La garde « échouer si zéro test »
   du round 1 créait un faux NÉGATIF (FAIL erroné sous Microsoft.Testing.Platform). Règle :
   tester « ça doit échouer quand c'est cassé » ET « ça ne doit pas échouer quand c'est sain »,
   y compris avec les variantes de format/environnement (.NET 10 vs net48, VSTest vs MTP).
