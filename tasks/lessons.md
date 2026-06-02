# Lessons Learned

_Ce fichier est mis à jour après chaque correction utilisateur ou erreur d'orchestration.
Il sert de mémoire pour éviter de répéter les mêmes erreurs. Les agents le lisent au début
de chaque session (protocol.md, Step 1)._

---

## 2026-06-02 — Leçon majeure : Conformat est un PRODUIT, pas un projet client

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

## 2026-06-02 — Spécifique Conformat (conformité fiscale)

- **Ne jamais inventer une règle fiscale.** Si la spec (docs/conception/) ne tranche pas une
  question TVA/VATEX/arrondi, l'item passe en `blocked` avec le nom de la décision manquante
  et son propriétaire (expert-comptable du client ou support PA). Deviner = risque fiscal client.
- **Les montants sont en decimal.** Tout float/double sur un montant est un P1, sans exception.
- **La piste d'audit et le coffre d'archive sont sacrés.** Aucun code ne doit pouvoir modifier
  ou supprimer un DocumentEvent ou un paquet d'archive, même pour « corriger » des données de test.
- **PowerShell 5.1 + fichiers UTF-8 sans BOM = corruption d'accents.** Les .ps1 doivent avoir
  un BOM UTF-8 ; les lectures/écritures de fichiers en .NET doivent spécifier l'encodage UTF-8
  explicitement.
