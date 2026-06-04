# ADR-0010 — Extraction de texte PDF pour la réconciliation : PdfPig (TRK07)

**Date :** 2026-06-04

**Statut :** Accepté (2026-06-04)

---

## Contexte

Le module `Reconciliation` (TRK07) rapproche les PDF du pool non lié (poussés par l'agent via
`/pdf-pool` quand l'adaptateur déclare `ProvidesUnlinkedDocumentPool`) des documents émis. La
**stratégie 2** (TRK07) cherche le **numéro de document dans le TEXTE du PDF** (confiance haute) —
elle exige donc d'extraire le texte d'un PDF. Tout nouveau package nécessite un ADR (blueprint.md §5).

Quatre contraintes encadrent ce choix :

1. **Licence compatible.** Le produit est commercial et propriétaire : la bibliothèque doit être sous
   licence permissive (MIT/Apache/BSD), jamais AGPL/GPL ni commerciale payante.
2. **Contrainte net48 LEVÉE.** La plateforme est en .NET 10 (le frein de licence/runtime net48 du
   backlog v5 n'existe plus). La bibliothèque doit cibler `net10.0` (ou `netstandard2.0`), sans
   dépendance native.
3. **Frontière de module.** L'extraction est consommée derrière un **port** (`IPdfTextExtractor`,
   couche `Application`) ; la bibliothèque concrète vit dans la SEULE couche `Infrastructure` du module
   Reconciliation (le moteur de rapprochement reste pur et testable, sans dépendance PDF).
4. **Pas d'OCR en V1.** Un PDF natif (texte) est lisible ; un PDF scanné (image) ne l'est pas. L'OCR
   (reconnaissance d'image) est hors périmètre V1 (dépendances lourdes, coût) — un PDF sans texte
   exploitable devient un **orphelin** (file d'attente manuelle), jamais une erreur.

## Décision

L'extraction de texte PDF utilise **PdfPig** (`UglyToad.PdfPig`), **licence Apache-2.0**, version
**`0.1.14`**, cible `netstandard2.0` (compatible .NET 10), **100 % managé** (aucune dépendance native).

La dépendance est **isolée dans le SEUL projet `Liakont.Modules.Reconciliation.Infrastructure`**
(`PdfPigTextExtractor`), derrière le port `IPdfTextExtractor` (couche `Application`). Le moteur de
rapprochement (`ReconciliationEngine`, `Domain`) reçoit le texte DÉJÀ extrait : il est pur, sans
dépendance PDF, entièrement testable.

L'extracteur respecte le contrat du port :

- extraction du texte page par page (`PdfDocument.Open` + `Page.Text`) ;
- un contenu **non-PDF, tronqué, chiffré ou malformé** ne lève **jamais** — il renvoie `null`, et le PDF
  devient un orphelin (une passe de réconciliation n'est pas interrompue par un fichier corrompu) ;
- un PDF **sans texte** (scanné) renvoie `null` (pas d'OCR en V1).

La qualité d'extraction n'est **pas critique pour la sûreté fiscale** : la stratégie 2 (numéro dans le
texte) est de confiance HAUTE mais reste un rapprochement DÉTECTÉ, pas une règle fiscale ; et la
stratégie 3 (date + montant) est de confiance MOYENNE, donc **toujours confirmée par un opérateur**
(décision 2026-06-02 — aucun lien automatique sous la confiance haute). PdfPig sert aussi en TEST à
**générer** des PDF de référence (`PdfDocumentBuilder`), ce qui exerce le round-trip extraction sans
fixture binaire opaque.

## Conséquences

- **Frontière respectée.** Le module ne dépend de PdfPig que dans son Infrastructure ; le moteur et les
  contrats restent purs. Remplacer la bibliothèque (ou ajouter l'OCR) n'impacte que `PdfPigTextExtractor`.
- **Licence sûre.** Apache-2.0 est compatible avec un produit propriétaire (contrairement à iText AGPL).
- **Pas d'OCR.** Les PDF scannés deviennent des orphelins (file manuelle) — comportement documenté, jamais
  un faux rapprochement. L'OCR pourra être ajouté en fast-follow derrière le même port, sans toucher au
  moteur.
- **Tests.** `PdfPigTextExtractor` est testé unitairement (PDF généré par `PdfDocumentBuilder` contenant
  un numéro connu → extraction → présence du numéro ; contenu non-PDF → `null`). Le moteur est testé
  séparément sur du texte fourni directement (aucune dépendance PDF).
- **Build.** Une seule `PackageVersion` centralisée (`Directory.Packages.props`), référencée par le seul
  projet `Reconciliation.Infrastructure`. Aucune donnée client embarquée (CLAUDE.md n°7).

## Alternatives écartées

- **iText 7** — extraction de texte excellente, mais licence **AGPL** (ou commerciale payante) :
  incompatible avec un produit propriétaire distribué. Éliminé sur la licence.
- **PDFsharp / MigraDoc** — orientés GÉNÉRATION/manipulation, pas extraction de texte fiable ; pas le bon
  outil pour lire un texte arbitraire.
- **Docnet.Core / PdfiumViewer (PDFium)** — extraction robuste mais **dépendance native** (binaire C++
  par architecture) : alourdit le déploiement de l'appliance et contredit la contrainte « 100 % managé ».
- **QuestPDF** (déjà présent pour la génération PDF, Common.UI) — bibliothèque de **génération**
  uniquement, n'extrait pas de texte. Conservée pour son usage actuel, non réutilisable ici.
- **OCR (Tesseract.NET) en V1** — nécessaire pour les PDF scannés, mais dépendance native lourde et
  périmètre non requis en V1 (les PDF scannés deviennent des orphelins). Reporté en fast-follow derrière
  le même port `IPdfTextExtractor`.
