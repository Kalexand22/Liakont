# Guide de restitution — export de réversibilité d'un client (OPS06a)

> Destiné à l'OPÉRATEUR d'instance qui exporte le dossier d'un client, et au DESTINATAIRE de
> l'export (le client, son nouvel éditeur, un vérificateur fiscal). L'export est **autonome** :
> il se lit et se vérifie **sans la plateforme Liakont**.

## 1. Produire l'export

Sur l'écran **Clients** (`/clients`, réservé à l'opérateur d'instance — permission
`liakont.supervision`), la ligne de chaque client porte l'action **« Exporter ce client »**.
Elle ouvre une confirmation (opération potentiellement lourde) puis télécharge un fichier
`reversibilite-<client>.zip`.

L'export est disponible pour **tout** client, **y compris désactivé** : la restitution de
**fin de vie** (résiliation, changement de logiciel) est précisément ce cas.

Le client choisi est exporté dans **son** périmètre (isolation par tenant) : l'export ne contient
jamais les données d'un autre client. Les **secrets ne sont jamais exportés** (les clés API des
Plateformes Agréées sont masquées — seul l'indicateur « clé saisie » figure).

## 2. Contenu du dossier (ZIP décompressé)

| Dossier / fichier | Contenu |
|---|---|
| `archive/` | Le **coffre d'archive** complet (WORM) : un paquet par document émis, sous `archive/<année>/<mois>/<numéro>/` (flux transmis à la PA, accusé + identifiants DGFiP, rendu lisible, **`manifest.json`** des empreintes et du chaînage), les **preuves d'ancrage** temporel (`_anchors/…`), et le **`rapport-integrite.json`** (vérification au moment de l'export). |
| `archive/notice-verification.txt` | Notice fiscale détaillant le contenu d'un paquet et la **procédure de vérification** d'intégrité. |
| `tracking/` | Le **suivi des documents** : `documents-NNNN.json` (par lots) + `index.json` (récapitulatif). |
| `parametrage/` | Le **paramétrage** du tenant : profil (raison sociale, SIREN, adresse), réglages fiscaux, comptes PA (**clés masquées**), table de mapping TVA, planification, seuils d'alerte. |
| `journal/` | Le **journal d'audit** opérateur (`audit.json`). |
| `notice-reversibilite.txt` | Notice de réversibilité (contenu, limites assumées). |

## 3. Vérifier l'intégrité de l'archive — HORS PLATEFORME

L'intégrité du coffre ne **dépend pas** de Liakont : elle repose sur une **chaîne d'empreintes
SHA-256** (chaque paquet scelle l'empreinte du précédent) et sur des **preuves d'ancrage temporel
RFC 3161**. On peut donc la re-vérifier avec des outils standard.

### 3.a — Outil fourni (recommandé)

Le script `tools/verifier-integrite-archive.ps1` (PowerShell, **sans dépendance Liakont**) recalcule
toute la chaîne depuis les seuls fichiers exportés et signale toute altération (pièce modifiée,
paquet supprimé / inséré / réordonné) :

```powershell
powershell -ExecutionPolicy Bypass -File verifier-integrite-archive.ps1 -ExportPath .\reversibilite-<client>
```

- Code de sortie **0** + « ARCHIVE INTÈGRE » : toutes les empreintes et le chaînage concordent.
- Code de sortie **1** + « ARCHIVE ALTÉRÉE » : au moins une incohérence (détaillée à l'écran).
- Code de sortie **2** : chemin introuvable ou dossier non reconnu.

### 3.b — Procédure manuelle (équivalente, sans l'outil)

La formule reproduite est exactement celle de la plateforme :

1. **Empreinte d'une pièce** = `SHA-256(octets du fichier)` (hexadécimal minuscule). Pour chaque
   fichier listé dans un `manifest.json`, recalculez son empreinte et comparez-la à la valeur du
   manifeste. Toute différence = pièce altérée.
2. **Empreinte d'un paquet** (`packageHash`) = `SHA-256` de la concaténation, **pour chaque pièce
   triée par nom (ordinal)**, de `«<nom>:<empreinte>\n»`. Un addendum à pièce unique a pour
   empreinte celle de sa pièce. Comparez au `packageHash` du manifeste.
3. **Chaînage** (`chainHash`) : `chainHash(N) = SHA-256( chainHash(N-1) + packageHash(N) )`, avec
   `chainHash(0) = ""` (chaîne vide) pour le premier paquet. Recalculez la chaîne dans l'ordre ;
   une rupture localise la première entrée altérée, supprimée ou réordonnée.
4. **Ancrage temporel** : les jetons `_anchors/…/anchor-*.tsr` attestent qu'à une date donnée la
   tête de chaîne portait son empreinte. Ils se vérifient avec tout outil RFC 3161 standard
   (`openssl ts -verify`) contre le certificat de l'autorité d'horodatage (TSA). Le manifeste
   d'ancrage (`*.json`) indique l'empreinte ancrée (`chainHeadHash`) et la méthode.

Encodage : tout est **UTF-8**, toute empreinte est en **hexadécimal minuscule**.

## 4. Limite assumée

Ce coffre n'est **pas** un système d'archivage électronique certifié NF Z42-013 / NF 461. Son
intégrité repose sur la chaîne SHA-256 et le scellement qualifié eIDAS (ancrage RFC 3161). La
certification NF Z42-013 n'est jamais revendiquée. Voir `archive/notice-verification.txt` (inclus
dans l'export) pour la notice fiscale complète.
