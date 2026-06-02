# SYNTHÈSE — Deep researches commerciales DR7-DR17

> Rédigée le 2026-06-02 après exécution des 11 deep researches du `Plan-DR-Marche-Commercial.md`.
> Tous les rapports détaillés sont dans ce répertoire (`DR7-*.md` à `DR17-*.md`).
> **Documents opérationnels dérivés :**
> - **`Offre-Editeur-Passerelle.md`** — le modèle grappe en détail (qui paie le connecteur, niveaux de partenariat, marges par site, pitch 3pi prêt à l'emploi, rétroplanning de campagne T4 2026-T1 2027).
> - **`Identification-Editeurs-Candidats.md`** — méthodologie pour constituer la liste des 10-15 éditeurs candidats (grappes 3-5) : sources (fédérations métier, NAF 5829C, signaux legacy), grille de qualification, fiches candidats. À exécuter T3 2026.

---

## Les 10 enseignements clés

### 1. Le positionnement produit est validé (DR7)
Personne en France ne fait de l'**extraction directe depuis bases legacy** : tous les acteurs (Tenor, TX2, Generix, PA) exigent que le système source sache exporter un fichier ou exposer une API. La niche est ouverte. Seul concurrent approchant : **DevVersPA/SEALOG**, limité à WinDev.

### 2. Le marché ne se mesure pas par les parcs logiciels, mais par les cohortes d'obligation (DR8)
Aucune statistique fiable sur les parcs legacy français n'existe. Le discours commercial doit s'appuyer sur : **7 343 GE/ETI** (échéance sept. 2026), **~4,2 M PME/TPE** (échéance sept. 2027), et **42 % des PME/TPE ne connaissent aucune PA** (OpinionWay, fév. 2026).

### 3. Ne jamais facturer au document (DR9)
Le prix au document s'est effondré (Super PDP : 0,0025 €/facture, gratuité fréquente). La valeur est dans le **setup d'intégration** (5-35 k€ selon complexité) et l'**abonnement de maintenance/veille** (1 200-3 600 €/an), pas dans le transit.

### 4. Le calendrier réglementaire est ferme (DR9)
Tous les amendements de report ont été rejetés ; la DGFiP a confirmé en mai 2026 : pas de report. Les scénarios peuvent être construits sur sept. 2026 / sept. 2027 sans risque significatif.

### 5. Scénarios de CA réalistes : 200 à 700 k€ cumulés 2026-2029 (DR9)
Prudent ~200 k€ / médian ~400 k€ / optimiste ~700 k€+ (nécessite de dépasser la contrainte solo). Le moteur scalable est la **grappe via éditeur**, pas le client final direct (cycles GE/ETI de 3-18 mois).

### 6. ISATECH est en redressement judiciaire (DR12 — vérifié manuellement)
TC Rennes, 7 janvier 2026, période d'observation jusqu'au **7 juillet 2026**, après cession de la branche Dynamics à AD Ultima (juin 2025). Le RJ n'est pas une faillite — le parc de ~100 études garde son éditeur et sa maintenance — mais ISATECH n'a aucune solution annoncée pour EncheresV6 et sa capacité R&D est réduite. **Conséquence : relancer ISATECH activement — l'offre CMP passe nécessairement par eux (autorisation sur leur logiciel, probablement exécution du projet) ; le RJ impose juste de ne pas attendre passivement leur retour.**

### 7. 3pi/CHEOPS est la cible partenariat n°1 (DR10 + DR11)
L'équivalent d'ISATECH sur la même niche SVV : **4 salariés**, partenaire SYMEV, **zéro communication sur la réforme**. Incapable de développer sa conformité seul. Notre adaptateur enchères est réutilisable quasi tel quel pour son parc. Cibles suivantes : **Agimarée** (criées/mareyage), **Vinéa/NSI-SADIMO** (viticulture).

### 8. Les canaux d'acquisition prioritaires : prescripteurs + SEO, pas YouTube (DR13, DR14, DR15)
- **Experts-comptables** = canal n°1 (81 % des entreprises s'appuient sur eux) — angle « on débloque vos clients techniquement bloqués ».
- **SYMEV** = canal naturel de la niche enchères (ISATECH et 3pi y sont déjà).
- **SEO longue traîne** = à lancer avant fin 2026 pour capter le pic 2027. Niches libres : AS400, Sage i7, Navision, Delphi, Access. Niche WinDev déjà prise (DevVersPA).
- **YouTube** = pas prioritaire (règle des 2 canaux pour un solo).

### 9. L'offre « réception » est un produit d'appel, pas un produit (DR16)
Sanctions réception faibles (500 € puis 1 000 €/3 mois après mise en demeure). Un « Pack conformité réception » à 500-1 500 € crée la relation client en 2026 pour vendre l'émission en 2027.

### 10. Iopole est la PA de secours à préparer (DR17)
B2Brouter reste la PA principale (staging validé), mais **Iopole est la seule à documenter publiquement les flux de paiement 10.2/10.4** — le point ouvert majeur de B2Brouter (F9). Action : ouvrir une sandbox Iopole pour tester (gratuit) et créer un levier de négociation.

---

## Plan d'action consolidé (toutes DR confondues)

### Immédiat (juin 2026)

| # | Action | Issue de |
|---|---|---|
| 1 | **Relancer ISATECH activement sur le dossier CMP** (passage obligé : autorisation + probablement exécution du projet) | DR12 |
| 2 | Vérifier la santé financière de 3pi (Pappers) | DR11 |
| 3 | Vérifier DevVersPA/SEALOG (produit réel ? prix ?) | DR7 |
| 4 | Choisir le nom de produit + réserver le domaine (bloque le SEO) | DR15 |
| 5 | Ouvrir une sandbox Iopole et tester les payloads du staging | DR17 |

### Été 2026 (pendant le développement du produit)

| # | Action | Issue de |
|---|---|---|
| 6 | Surveiller le BODACC ISATECH (~7 juillet : issue de la période d'observation) | DR12 |
| 7 | Créer les 5 premières pages du site (problème + AS400 + Sage i7) | DR15 |
| 8 | Demander les conditions reseller B2Brouter (AC1) + partenaire Iopole (AC2) | DR9, DR17 |
| 9 | Demander les tarifs d'adhésion FNFE-MPE | DR13 |
| 10 | Clarifier le routage réception sans PA avec B2Brouter (lien ticket A2) | DR16 |

### À la démo prête (T3-T4 2026)

| # | Action | Issue de |
|---|---|---|
| 11 | **Contacter 3pi** avec le pitch partenariat marque blanche | DR11 |
| 12 | Contacter SYMEV pour les conditions de partenariat | DR13 |
| 13 | Site web complet en ligne (20 pages) — impératif avant fin 2026 | DR15 |
| 14 | Approcher 5-10 cabinets d'expertise comptable (angle « client bloqué ») | DR13 |
| 15 | Contacter Agisoft Engineering (Agimarée) — 2e partenariat vertical | DR11 |

### 2027 (pic PME/TPE)

| # | Action | Issue de |
|---|---|---|
| 16 | Lancer le Pack réception comme produit d'appel | DR16 |
| 17 | JFE 2027 en visiteur | DR13 |
| 18 | Évaluer NSI-SADIMO/Vinéa (sous-traitance module conformité) | DR11 |

---

## Limites méthodologiques de l'ensemble

1. **La vérification adversariale des DR a été massivement défaillante** (problème technique récurrent : abstentions 0-0). Les DR7, DR8, DR9 ont une vraie vérification ; les DR12-DR17 reposent sur des sources primaires cohérentes mais non re-vérifiées. Les éléments critiques (RJ ISATECH, profil 3pi, partenaires SYMEV) ont été **vérifiés manuellement** en compensation.
2. **Les conditions commerciales des PA restent inconnues** (non publiques) → AC1/AC2 indispensables.
3. **Les scénarios de CA sont des ordres de grandeur**, pas des projections : à recalibrer après les 5 premiers déploiements réels.
4. **Toutes les vérifications « aucune communication sur la réforme » datent du 2026-06-02** : un éditeur peut publier à tout moment. Re-vérifier avant chaque contact.

## Correspondance DR ↔ fichiers

| DR | Fichier | Solidité |
|---|---|---|
| DR7 | `DR7-Concurrence-Directe-Legacy.md` | 5/25 confirmés + abstentions |
| DR8 | `DR8-Dimensionnement-Marche-Adressable.md` | 6/25 confirmés (INSEE/OpinionWay solides) |
| DR9 | `DR9-Business-Model-Pricing-Scenarios-CA.md` | **16/25 confirmés** (la plus solide) |
| DR10 | `DR10-Secteurs-Editeurs-Legacy.md` | 17/25 confirmés mais synthèse plantée |
| DR11 | `DR11-Analyse-Editeurs-Cibles.md` | **Vérification manuelle directe** |
| DR12 | `DR12-Intelligence-ISATECH.md` | Abstentions + **RJ vérifié manuellement** |
| DR13 | `DR13-Canaux-Prescripteurs.md` | Abstentions (sources primaires cohérentes) |
| DR14 | `DR14-Acquisition-YouTube.md` | Abstentions (sources cohérentes) |
| DR15 | `DR15-SEO-Site-Web-Longue-Traine.md` | Abstentions (+ DevVersPA 1-0) |
| DR16 | `DR16-Offre-Reception-Produit-Appel.md` | 3 confirmés (sanctions réception) |
| DR17 | `DR17-Strategie-Multi-PA-Partenaires.md` | Abstentions (cohérent avec RECAP interne) |
