# DR9 — Business model / pricing / scénarios de CA

> Deep research exécutée le 2026-06-02 (105 agents, 23 sources, 104 claims extraits, 25 vérifiés).
> Plan parent : `Plan-DR-Marche-Commercial.md`. S'appuie sur DR7 (créneau peu saturé) et DR8 (cohortes top-down).
> **Solidité : 16/25 claims confirmés — la meilleure note des DR commerciales à ce stade.**

---

## Verdict synthétique

Trois enseignements structurants sortent de cette DR :

1. **Ne PAS facturer au document** — le prix à l'acte s'est effondré (Super PDP : 0,0025-0,01 €/facture). La valeur de la Passerelle est dans l'intégration et la maintenance, pas le transit de factures.
2. **Ancrer le prix sur l'évitement de sanction**, mais en restant bien en dessous du plafond (15 000 €/an) — quelques centaines à ~2 000 €/an pour une PME, prime d'un ordre de grandeur pour une GE (le forfait CMP à 33 k€ se justifie par la complexité d'extraction, pas par l'amende).
3. **La grappe via éditeur est le moteur scalable** — cycles GE/ETI = 3-18 mois, incompatibles avec un solo qui doit aussi produire. La grappe (~100 études via un éditeur) = déploiement packagé après le premier, coût marginal quasi nul, CAC mutualisé.

---

## 1. Contexte réglementaire — risque de report quasi nul ✅ 3-0

Le calendrier est **ferme** :
- 1er sept. 2026 : émission + réception GE/ETI ; réception toutes entreprises
- 1er sept. 2027 : émission + e-reporting PME/TPE/micro

Amendements de report (Sénat I-1413 nov. 2024 + Assemblée 11 avr. 2025) : **rejetés**. DGFiP (Amélie Verdier, plénière FNFE-MPE du 6 mai 2026) : *« Il n'est pas du tout prévu de report, ni sur la partie facture, ni sur la partie reporting. Cette réforme est prête. »* Pilote national lancé le 27 fév. 2026.

La clause de sauvegarde (report possible au 1er déc. 2026 pour GE/ETI) existe encore mais **aucun signal ne suggère son activation**. Les scénarios de CA peuvent être construits sur ces dates.

---

## 2. Benchmarks de pricing du marché — ce qui est CONFIRMÉ

### Prix au document : effondré ✅ confirmé (2-1/3-0)

| Acteur | Tarif document | Commentaire |
|---|---|---|
| **Super PDP** | 0,0025–0,01 €/facture (gratuit jusqu'à 1 000/mois) | Référence publique la plus basse |
| **PA génériques** | Gratuité fréquente | Conditionnée à l'abonnement logiciel ou limitée en volume |
| ~~Weproc PA Connect~~ | ~~0,15 €/document~~ | Réfuté comme représentatif du marché — hors bande |
| ~~Cegid~~ | ~~0,10–0,50 €~~ | **RÉFUTÉ (0-3)** — ne pas citer |

**Conclusion** : facturer au document n'est pas viable pour la Passerelle. Le marché a déjà commoditisé ce niveau.

### Setup / intégration ✅ confirmé (2-1)

| Type | Fourchette | Source |
|---|---|---|
| Setup standard PME | 200–2 000 € | Saasforge, comparateur-efacturation |
| Migration de données | 500–3 000 € | Saasforge |
| Formation | 300–1 000 € | Saasforge |
| PME/ETI à intégration ERP complexe | jusqu'à ~5 000 € | comparateur-efacturation |
| **Passerelle legacy sur-mesure (CMP)** | **~33 000 €** | Donnée d'entrée — 1 ordre de grandeur au-dessus = prime de rareté du créneau |

Le forfait CMP à 33 k€ ne se justifie pas par les sanctions mais par la **complexité d'extraction directe base legacy** — c'est le signal que le créneau peu saturé justifie un premium réel.

### Sanctions — ancre psychologique ✅ confirmé (3-0)

| Infraction | Amende | Plafond |
|---|---|---|
| Facture non émise en format conforme | **50 €/facture** | 15 000 €/an/entreprise |
| Transmission e-reporting manquante | **500 €/transmission** | 15 000 €/an/entreprise |

**Posture DGFiP** : objectif d'adhésion, pas de sanctions immédiates ; droit à l'erreur pour bonne foi documentée. **Implication pricing** : l'argument évitement de sanction est réel mais plafonné. Pour une PME, un abonnement de conformité doit rester très en dessous de 15 000 €/an pour avoir un ROI évident. Pour une GE (CMP), le risque n'est pas seulement l'amende mais le blocage opérationnel → justifie le premium.

---

## 3. Modèles économiques

### Contraintes du solo

- Cycles de vente GE/ETI : **3 à 18 mois** ✅ confirmé (3-0) — 1 à 3 deals type CMP par an au maximum.
- CAC B2B SaaS de niche : **300–5 000 USD** ✅ (3-0) — ratios LTV/CAC cibles 3:1 (norme) → 5-6:1 (prudent sur marché à churn élevé).
- **La grappe via éditeur court-circuite ce problème** : 1 deal éditeur = 100 clients potentiels, CAC mutualisé, cycle de vente sur l'éditeur (pas les 100 études une par une).

### Structure de pricing recommandée

**Canal A — Client final direct (type CMP)**

| Composante | Montant recommandé | Logique |
|---|---|---|
| **Forfait setup** (extraction + mapping + tests) | **5 000–35 000 € HT** selon complexité | CMP = borne haute (Magic XPA/Pervasive sur-mesure) ; PME standard legacy SQL = borne basse |
| **Abonnement annuel** (maintenance, veille réglementaire, évolutions normes AFNOR) | **1 200–3 600 €/an HT** (100–300 €/mois) | Bien sous le plafond de sanction de 15 k€ → ROI évident |
| **Volume e-reporting** (option) | **Inclus** dans l'abo ou 0,03–0,05 € au-delà d'un palier élevé | Ne pas reproduire le tarif PA — si volumétrique, rester marginal |

**Canal B — Grappe via éditeur/marque blanche**

⚠️ Aucun benchmark fiable de taux de royalties n'a survécu à la vérification adversariale (tous réfutés). La structure à négocier :

| Composante | Suggestion | Logique |
|---|---|---|
| **Forfait packagé par déploiement** (études après la 1re) | **500–2 000 € HT/étude** | Coût marginal quasi nul après industrialisation ; rentabilité dès la 2e étude |
| **Redevance annuelle de maintenance** | **600–1 200 €/an/étude** | Récurrent, veille réglementaire, corrections |
| **Partage avec l'éditeur** | **À négocier** (10–30 % ? — hypothèse de travail) | Voir AC1 (contact commercial direct B2Brouter/éditeur). La part éditeur rémunère AUSSI le support N1 (voir ci-dessous), pas seulement l'apport de clients |

### Répartition du support en grappe éditeur (structurant — ajouté le 2026-06-02)

Le support se découpe en trois niveaux ; **le N1 est porté par l'éditeur** — c'est une condition de viabilité du modèle pour un solo, et une contrepartie de sa part de revenus :

| Niveau | Qui | Contenu | Volume estimé |
|---|---|---|---|
| **N1 — Usage** | **Éditeur** | « Comment je fais ? », statuts, formation utilisateurs. Le client final appelle sa hotline habituelle (marque blanche : il ne sait pas que la Passerelle existe) | ~80 % des tickets |
| **N2 — Technique applicatif** | **Passerelle** | Rejets PA, statuts non remontés, erreurs de mapping, données source incohérentes | ~15-20 % |
| **N3 — PA / réglementaire** | **Passerelle + escalade PA** | Pannes API, évolutions de format AFNOR, questions fiscales | ~1-5 % |

**Prérequis produit/contrat pour que ce découpage tienne :**
1. **Tableau de bord de statuts** accessible au support de l'éditeur (sinon son N1 escalade tout) ;
2. **FAQ/base de connaissances + formation** du support éditeur (~½ journée) ;
3. **Matrice d'escalade contractuelle** : périmètre N1/N2, SLA, canal d'escalade (ticket, pas téléphone) ;
4. **Monitoring proactif** côté Passerelle : détecter les rejets avant l'appel client.

En Canal A (direct), la Passerelle porte tous les niveaux de support — c'est une des justifications de l'écart de prix entre les deux canaux.

### Coût total client final (TCO) — contrainte d'acceptabilité (ajouté le 2026-06-02)

⚠️ **Le prix qui compte pour le client final n'est pas le nôtre : c'est le total Passerelle + PA.** Les entreprises sous logiciel legacy ne roulent généralement pas sur l'or — le TCO doit rester sous un seuil psychologique.

**Tarifs PA réels vérifiés (B2Brouter eDocExchange, captures du 2026-05-28)** :

| Palier | Volume/mois | Prix/mois HT | Transaction supplémentaire |
|---|---|---|---|
| M0 | 1-50 | 15 € | 0,435 € |
| M1 | 51-100 | 29 € | 0,435 € |
| M2 | 101-300 | 59 € | 0,295 € |
| M3 | 301-600 | 89 € | 0,222 € |
| M4 | 601-1 500 | 169 € | 0,169 € |
| Compte supplémentaire | — | 150 € one-time | — |

Note : une « transaction » B2Brouter = tout eDocument envoyé, reçu ou importé → émission + réception + e-reporting cumulent. Une étude SVV type sera en M2-M3 (~700-1 100 €/an de PA).

**⚠️ Risque de comptage B2Brouter (découvert le 2026-06-02)** : leur guide général des transactions stipule qu'**un ledger de N enregistrements = N transactions facturables**. Si cette règle s'applique aux ledgers e-reporting Flux 10 (non précisé dans leur doc DGFiP), une étude SVV avec 500-2 000 bordereaux B2C/mois dépasse le palier M4 → **2 000+ €/an de PA rien qu'en e-reporting**. Question à poser à B2Brouter (action DR17-A5) — la réponse conditionne leur viabilité pour la niche enchères.

**Tarifs Super PDP vérifiés (superpdp.tech/tarifs, 2026-06-02)** : l'API est payante (la gratuité < 1 000 fact./mois ne vaut que pour le compte web manuel) mais dérisoire : **0,01 €/facture** + e-reporting compté **1 facture par journée d'encaissements quel que soit le volume**. Coût PA d'une étude type : **~3-5 €/mois (~40-60 €/an)**.

**TCO comparé (client grappe, étude type)** :

| Composante | Avec B2Brouter | Avec Super PDP (API) |
|---|---|---|
| Setup Passerelle + compte PA | 650-2 150 € | 500-2 000 € |
| PA récurrente | 700-2 000+ €/an (selon comptage ledgers) | **~40-60 €/an** |
| Récurrent annuel total (Passerelle + PA) | ~1 300-3 200 €/an | **~650-1 250 €/an** |

**Conséquence structurante : deux offres packagées, et l'Offre Éco devient « PA incluse »** :
- **Offre Éco (PA = Super PDP en marque grise)** : le coût PA est si faible qu'il est **absorbé dans l'abonnement Passerelle**. Le client a **UN contrat, UNE facture, zéro démarche PA** → « conformité complète, PA incluse, **< 100 €/mois tout compris** ». Condition d'accès aux petites études/PME. Super PDP propose précisément la marque grise avec tarification grossiste pour ce montage.
- **Offre Intégrale (PA = B2Brouter)** : réception intégrée, international/Peppol, fonctionnalités avancées — 110-160 €/mois (sous réserve de la réponse sur le comptage des ledgers).

**Archivage réglementaire dans l'offre (ajouté le 2026-06-02)** : la conservation 10 ans (C. com. L123-22) reste la responsabilité de l'assujetti — la PA n'a aucune obligation d'archiver. Super PDP annonce un archivage 10 ans avec intégrité (a priori inclus) ; B2Brouter ne précise rien. **Recommandation : la Passerelle garde sa propre copie archivée (intégrité + horodatage) et l'inclut dans l'abonnement** → ① argument commercial (« archivage 10 ans inclus »), ② protection du client contre la disparition/le changement de PA (réversibilité), ③ facteur de rétention (le client qui a 10 ans d'archives chez nous ne part pas). Coût marginal pour nous : négligeable (quelques Go/client).

Notre marge est quasi identique dans les deux cas ; ce qui change, c'est l'accessibilité du marché et la simplicité contractuelle. Vérifications restantes : flux paiement 10.2/10.4 + archivage NF Z42-013 chez Super PDP, comptage ledgers + archivage chez B2Brouter (DR17-A4/A5).

---

## 4. Scénarios de CA 2026–2029 (dérivation analytique — confidence medium)

> ⚠️ Ces scénarios sont des **ordres de grandeur** construits à partir des données confirmées + hypothèses explicites. Ils ne sont pas des projections sourcées.

### Hypothèses communes

| Paramètre | Valeur |
|---|---|
| Capacité solo | ~200 j facturables/an |
| Déploiement GE sur-mesure | 20-40 j (1-3 déals/an possibles) |
| Déploiement packagé grappe | 2-5 j/étude (après industrialisation) |
| Rétention abonnés | 3-5 ans (churn érosion legacy intégré) |
| Saisonnalité | Pic H2 2026 (GE/ETI) + H1 2027 (PME/TPE — pic de demande) |

### Scénario PRUDENT

- 2026 : CMP (~33 k€ setup) + amorçage grappe (10 études à ~1 000 € = 10 k€ setup + 6 k€/an abo) = **~45 k€ HT**
- 2027 : 1 nouveau GE/ETI (~15 k€) + montée grappe à 30 études + abonnements cumulés = **~60-70 k€ HT**
- 2028-2029 : plateau récurrent (abonnements + TMA) ~30-40 k€/an ; nouveaux déploiements ponctuels

**CA cumulé 2026-2029 : ~200 k€ HT** — essentiellement du setup one-shot avec une base récurrente émergente.

### Scénario MÉDIAN

- 2026 : CMP + 1 autre GE/ETI + 20 études grappe = **~80 k€ HT**
- 2027 : pic PME/TPE pré-sept. 2027, 2 autres GE/ETI, 50 études grappe cumulées = **~120-140 k€ HT**
- 2028 : abonnements récurrents 50+ études + 1-2 gros comptes/an = **~80-100 k€ HT récurrent + setup**
- 2029 : base stable, veille ViDA 2030 valorisée dans l'abo

**CA cumulé 2026-2029 : ~400 k€ HT** — le récurrent représente 30-40 % du CA en 2028-2029.

### Scénario OPTIMISTE

- 2026-2027 : 2-3 grappes d'éditeurs différents (enchères + 1-2 autres secteurs de DR10) = **100-200 déploiements packagés + 3-4 GE/ETI** → **~200-250 k€ HT/an au pic**
- *Condition : sous-traitance ou recrutement nécessaire — le solo est le goulot*
- Récurrent post-pic : **~100 k€+/an** sur la base d'abonnements

**CA cumulé 2026-2029 : ~700 k€+ HT** — mais nécessite de dépasser la contrainte solo.

### Seuils de décision issus des scénarios

| Signal | Action |
|---|---|
| Grappe enchères > 30 déploiements en 2026 | Signal de réplicabilité → prospecter 1-2 autres éditeurs verticaux (DR10) |
| Setup GE/ETI > 50 % du CA en 2027 | Risque de dépendance à quelques deals — renforcer le canal grappe/récurrent |
| Temps de déploiement packagé > 5 j/étude | Sous-productiser avant de scaler |
| Demande > 150 j/an | Décision sous-traitance ou recrutement |

---

## 5. Questions ouvertes

1. **Taux de partage éditeur/marque blanche** — pas de benchmark fiable ; à négocier directement (voir AC1 du plan).
2. **Plafond 15 000 €** — s'applique-t-il séparément par catégorie (e-invoicing vs e-reporting) ou de façon combinée ?
3. **Jours réels par déploiement packagé grappe** — à mesurer sur les 5 premières études après CMP.
4. **Cycle de vente sur les PME via canal éditeur** — vraisemblablement plus court que les 3-18 mois GE (l'éditeur fait le premier niveau de qualification).

## 6. Données techniques de la recherche

- **Stats** : 5 angles, 23 sources, 104 claims extraits, 25 vérifiés → **16 confirmés, 9 tués** (meilleure solidité parmi les DR commerciales).
- **Réfutations nettes** : prix au document >0,10 € (0-3), abonnements micro-entreprises 19-50 €/mois (0-3), fourchettes royalties marque blanche US (0-1 à 0-3) — tous à ne pas citer.
- **Sources primaires exploitées** : senat.fr (amendements), compta-online (plénière DGFiP mai 2026), service-public.gouv.fr (sanctions), Légifrance (Art. 1737, 1788D), Stripe (benchmarks CAC/LTV), superpdp.tech (tarif primaire).
