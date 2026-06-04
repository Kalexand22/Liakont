# ADR-0012 — Acquittement agent en deux temps : création + réconciliation par statut

**Date :** 2026-06-05

**Statut :** Accepté (2026-06-05)

**Amende :** F12 §3.3 (sémantique des réponses agent), §4.1 (dédoublonnage), §2.2 (purge de la file locale de l'agent)

---

## Contexte

L'ingestion (PIV04) acquitte un document poussé par l'agent en **deux gestes séparés**
(`IngestDocumentBatchHandler`) :

1. **Transaction durable** : inscription au registre `received_documents` (qui ne garde que
   l'empreinte `payload_hash`, pas le payload) + écriture de l'événement outbox
   `DocumentReceivedV1` (lui aussi sans payload) ;
2. **Après commit**, création **best-effort** du document `Detected` (port `IDocumentIntake`), dont
   l'échec est **avalé** (log seul) — la réponse reste `accepted`.

La spec actuelle (F12 §3.3) fait **purger la file locale de l'agent dès le 200** (`accepted`/`duplicate`),
et l'agent jette alors sa seule copie complète du document (file SQLite, F12 §2.2). Conjugué au geste 2
best-effort, cela ouvre une **perte silencieuse** : sur un échec transitoire de la création (hoquet de
la base tenant), le document est *inscrit* (empreinte seule) mais **jamais rangé** dans le pipeline,
l'agent reçoit `accepted` et **supprime sa copie**, et la reprise promise n'existe pas :

- `DocumentReceivedV1` ne porte que l'empreinte → le document est **irreconstructible** ;
- **aucun consommateur** de `DocumentReceivedV1` n'existe (prévu pour PIP01, segment `pipeline` non
  construit) ;
- un **renvoi** serait écarté en `duplicate` par le dédoublonnage sur `payload_hash` (F12 §4.1) **sans
  re-tenter le rangement**.

→ Document fiscal accepté mais absent du pipeline, **définitivement perdu** — donc jamais déclaré à
l'administration, ce qui engage la responsabilité fiscale du client (CLAUDE.md n°3 et n°4). **Latent
aujourd'hui** (le transport AGT et le pipeline PIP01 ne sont pas construits, aucun document réel ne
circule), mais **structurel** dès que ces deux maillons existeront.

La cause racine n'est pas « il manque une copie durable » : c'est que **le premier retour de la
plateforme veut dire à la fois "reçu" et "tu peux supprimer"**, alors que le rangement durable n'est pas
encore garanti à cet instant.

## Décision

L'acquittement agent passe d'un temps (« reçu = supprime ») à **deux temps + réconciliation par
statut** (modèle *at-least-once* + idempotence) :

1. **Création (push).** `POST /api/agent/v1/documents/batch` signifie désormais **« reçu, pris en
   charge »**, et non « durablement traité ». L'agent marque l'élément **« en cours »** localement et
   **ne le re-pousse pas** à chaque tic.
2. **Réconciliation (statut).** **Avant** chaque push, l'agent interroge un **point de statut**
   (`GET /api/agent/v1/documents/status`, lecture seule, tenant-scopé, clé `(source_reference,
   payload_hash)`) et agit selon l'état RAPPORTÉ par la plateforme :
   - **`Processed` (terminal OK)** → l'agent **purge** l'élément de sa file ;
   - **`Rejected` (terminal)** → l'agent **purge + signale à l'opérateur** (un payload non conforme au
     contrat ne se re-pousse pas — pas de boucle infinie) ;
   - **`Pending` / inconnu (non terminal)** → l'agent **renvoie** l'élément au prochain push.

**« OK terminal »** = **le document est durablement créé sur la plateforme ET entré dans le pipeline**
(le `Detected` existe). Ce n'est PAS « transmis à la Plateforme Agréée » (process asynchrone long, que
l'agent n'a pas à attendre). Le point de coupure est : « la plateforme a pris la responsabilité du
document ».

**L'agent n'a aucune logique métier** (frontière blueprint §2 / CLAUDE.md n°6) : il LIT un statut et
applique une règle mécanique (purger / renvoyer / signaler), il n'INTERPRÈTE jamais l'état fiscal.

### Conséquence sur le dédoublonnage (amende F12 §4.1)

Le `duplicate` doit cesser d'être monolithique et **distinguer** :

- **déjà rangé** (un `Detected` existe pour ce `(source_reference, payload_hash)`) → terminal, aucun
  effet (vrai doublon) ;
- **reçu mais non rangé** → **re-tenter le rangement** (idempotent sur `DocumentId` / `payload_hash`).

C'est CE point qui **ferme la fuite** : un renvoi d'un document non rangé est rejoué, pas écarté. Le
point de statut reflète exactement la même vérité (Detected présent ou non).

La création best-effort post-commit de PIV04 **reste** un fast-path acceptable **sous ce protocole** :
son échec n'est plus une perte, puisque l'agent conserve l'élément jusqu'à confirmation, et que le
renvoi re-tente le rangement. Le déclencheur durable reste l'événement outbox.

## Conséquences

- **AGT (agent, lot futur)** : file locale qui **conserve** un élément tant que son statut n'est pas
  terminal ; marquage « en cours » ; **appel du point de statut avant chaque push** ; purge sur
  `Processed` / `Rejected` (avec signalement opérateur sur `Rejected`).
- **Ingestion / API (lot futur)** : nouveau point `GET /api/agent/v1/documents/status` (lecture seule,
  tenant-scopé) ; **redéfinition documentée** du sens de la réponse de lot (`accepted` = « en cours »,
  pas « supprime ») ; **affinage du dédoublonnage** (re-tenter le rangement d'un non-rangé).
- **PIP01 (pipeline, lot futur)** : définit l'état « OK terminal » rapporté par le statut (`Detected`
  créé et entré dans le pipeline).
- **Idempotence** : `IDocumentIntake` est déjà idempotent sur `DocumentId` (contrat de cohérence) ; le
  statut et le ré-essai s'appuient sur la clé `(source_reference, payload_hash)`, déjà l'identité de la
  file agent (F12 §2.2) et du registre de réception.
- **Rien à coder dans le segment core-foundation** : le protocole exige l'agent (AGT) **et** la
  sémantique d'état terminal (PIP01), non construits. Un marqueur **`TODO(ADR-0012)`** est posé au point
  d'avalement actuel et au retour `duplicate` de `IngestDocumentBatchHandler`, pour qu'aucune
  « correction » intermédiaire ne rende l'intake bloquant (ce qui ré-introduirait le risque d'orphelin)
  avant que le protocole complet ne soit en place.
- **Aucune dépendance d'infrastructure ajoutée** (pas de courtier de messages — cf. alternatives).

## Alternatives écartées

- **Pile de messages (Kafka / RabbitMQ).** Conçue pour ce besoin (livraison durable, rejeu), mais **trop
  lourde** pour la topologie cible : appliance on-prem chez le client + agent léger en **HTTPS sortant
  uniquement** (F12 §2). Exploiter et superviser un broker chez chaque client est disproportionné ; la
  réconciliation par statut sur HTTP atteint le même résultat (aucune perte, aucun doublon) en restant
  « sortant uniquement » et sans nouvelle brique d'infrastructure.
- **Rendre l'intake bloquant** (créer le `Detected` dans la transaction de réception). Ré-introduit le
  risque de **document orphelin** si l'inscription échoue ou entre en course — précisément ce que le
  best-effort post-commit évitait (`IDocumentIntake`, prérequis BLOQUANT de TRK02) — et couple
  Ingestion ↔ Documents dans une même transaction. Non retenu.
- **Enrichir `DocumentReceivedV1` du payload pivot complet + bâtir un consommateur de reprise.**
  Possible (add-only), mais plus lourd (duplication du payload dans l'outbox + nouveau consommateur à
  écrire) et ne dispense pas de définir l'état terminal. La réconciliation par statut garde **l'agent
  comme tampon** (il détient déjà la donnée source) et reste plus simple à exploiter.
