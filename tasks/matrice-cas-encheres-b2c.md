# Matrice des cas de figure à tester — Enchères B2C/B2B (recette Isatech)

> But : avoir en tête TOUS les cas métier avant de les seeder en base source (EncheresV6_Demo).
> Ancré sur le seed `deployments/encheres-demo/` (mapping-tva.json, inject-demo-siren.sql) + F03.
> ⚠️ Le fiscal reste à valider par Karl (expert) — ceci décrit **ce que le build gère aujourd'hui**, pas une vérité fiscale.

## Deux axes qui déterminent tout

**1. Famille de pièce** (préfixe `SourceReference`, BUG-20) :
- **BA** — Bordereau **Acheteur** : ce que paie l'acheteur (adjudication + honoraire acheteur). **Pièce maîtresse du reporting** (marge / taxable / export / B2B).
- **BV** — Bordereau **Vendeur** : décompte du vendeur (produit de vente − honoraire vendeur). Son honoraire alimente le **calcul de la marge**, mais le BV comme document (389 / honoraire-vendeur B2B) = **axe non codé** (voir §Gaps).
- **Facture client** / **Note d'honoraires** : documents **ordinaires** hors enchères (clé = **taux effectif**, TT-81 dérivé de la nature du tenant).

**2. Canal** = piloté par l'**identité de l'acheteur** (document-driven, pas par la capacité PA) :
- **B2C** (acheteur particulier ou non identifié) → **e-reporting** (agrégé ou unitaire selon le régime).
- **B2B** (acheteur avec **SIREN** valide) → **voie document** (e-invoicing Factur-X / PDP), **jamais** e-reporté.

> **Marge (régime 6)** = 2 jambes : **honoraire acheteur TTC + honoraire vendeur TTC** (art. 297 E). La TVA porte sur la marge, jamais sur l'adjudication (exonérée E+VATEX-EU-J).

---

## Matrice

Statut : ✅ présent au dataset · 🟡 présence à confirmer · ⛔ à injecter · 🚧 gap non codé (ne pas attendre fonctionnel)

### A. Tenant `volontaire` (ventes volontaires SVV, dossier 2) — B2C dominant
| # | Famille | Régime source | Acheteur | Canal → Marquage → Sortie | Repère démo | Statut |
|---|---|---|---|---|---|---|
| 1 | BA | **6** (marge) | particulier | B2C → **TMA1** → agrégé jour×devise×taux | **100152**, 100022 | ✅ |
| 2 | BA | **5** (taxable) | particulier | B2C → **TLB1** (prix total) → agrégé | un BA rég. 5 | ✅ |
| 3 | BA | export **hors UE** (EXP_HORSUE) | particulier | B2C → G/VATEX-EU-G → **TLB1 unitaire** taux 0 | BA livré hors UE | 🟡 |
| 4 | BA | export **intracom** (EXP_CEE) | particulier | B2C → K/VATEX-EU-IC → **TNT1 unitaire** taux 0 | BA livré CEE | 🟡 |
| 5 | BA | export **franchise** (EXP_FR) | particulier | B2C → G → **TLB1 unitaire** taux 0 | BA franchise export | 🟡 |
| 6 | Facture client | taux **20 %** | particulier | B2C ordinaire → S 20 % → **TPS1** → agrégé | **00100004** | ✅ |
| 7 | Facture client | taux **5,5 %** | particulier | B2C ordinaire → S 5,5 % → **TPS1** → agrégé | facture 5,5 % | ⛔ (à injecter si absente) |
| 8 | Note d'honoraires | taux **20 %** | particulier | B2C ordinaire → S 20 % → **TPS1** → agrégé | **100008** | ✅ |
| 9 | BA | 6 (marge) | **pro FR + SIREN** | **B2B aiguillage** → voie document Factur-X (adjudication E + honoraire acheteur en ligne), jamais e-reporté | **2000026** (VARRA 998877666) | ✅ |
| 10 | BA | 5 (taxable) | **pro FR + SIREN** | **B2B** → e-invoicing | BA société à SIREN | ✅ |

### B. Tenant `judiciaire` (criée SCP, dossier 1) — B2B (criée = 100 % B2B)
| # | Famille | Régime | Acheteur | Canal → Sortie | Repère démo | Statut |
|---|---|---|---|---|---|---|
| 11 | BA criée | 5/6 | **pro FR + SIREN sandbox** | **B2B Factur-X** → SuperPDP `/convert` → Émis | **9000004** (Tricatel 000000001) | ✅ |
| 12 | BA criée | 5/6 | pro FR + **SIREN réel** 967890120 | B2B → SuperPDP **rejette** « address not in peppol directory » (limite annuaire **sandbox**, pas un bug) | **9000003** (GOURRIER) | ✅ (limite connue) |

### C. Cas « étranger » / pays
| # | Famille | Cas | Attendu | Repère démo | Statut |
|---|---|---|---|---|---|
| 13 | BA | acheteur pays **« ENG »** (non-ISO) | normalisé **GB**, non bloqué | **100264** | ✅ |
| 14 | BA | acheteur pays **« JAP »** (non-ISO) | normalisé **JP**, non bloqué | **2000020** | ✅ |
| 15 | BA | acheteur **assujetti UE** (n° TVA BE) **sans SIREN** | **fail-close** « facture B2B requise » — jamais e-reporting domestique ni B2C anonyme | **100351** | ✅ |
| 16 | BA | code pays **inconnu** (« XYZ ») | **bloqué** (fail-closed, jamais deviné) | — | ⛔ (à injecter) |

### D. Cas bloquants / contrôles (fail-closed — « bloquer plutôt qu'envoyer faux »)
| # | Famille | Cas | Attendu | Repère démo | Statut |
|---|---|---|---|---|---|
| 17 | BA | société FR **sans SIREN** (IsCompanyHint) | bloqué **BUYER_LOOKS_PROFESSIONAL** + verdict opérateur | sociétés rang ≥ 5 | ✅ |
| 18 | BA/BV | **régime non mappé** (1/2/3/7/8/9/0) | **bloqué au CHECK** « régime absent de la table validée » | docs à ces régimes | ✅ |
| 19 | Note/Facture | **taux mixte** (honoraires 20 % + frais 0 %) | **bloqué** (le 0 % non mappé) | **100004** | ✅ |
| 20 | BA | ligne **sans code régime** en source | bloqué, message VRAI + « corrigez la donnée source » | ligne réellement sans régime | 🟡 (à confirmer/injecter) |
| 21 | Facture B2B | **mentions FR vides** | **bloqué au CHECK** (BUG-26b), pas d'envoi voué au rejet | 9000004 mentions vides | ✅ |
| 22 | BA | **SIREN sandbox** 000000001/000000002 | accepté **par dérogation gâtée par l'env** (Sandbox oui / Production non) | 9000004 | ✅ (BUG-23 ❓) |

### E. Gaps — axes NON codés (à ne pas attendre fonctionnels ; utiles à connaître pour le seed)
| # | Cas | Pourquoi gap | Réf |
|---|---|---|---|
| 23 | 🚧 **BV** honoraire vendeur **assujetti** (rég. 5) → aiguillage B2B office→vendeur | `vendeur_siren` préparé en base mais **l'agent ne le lit pas encore** ; exige un document « honoraire-seul » distinct | inject-demo-siren.sql §5-8, F03 §2.7 |
| 24 | 🚧 **BV / décompte 389** (vente du bien sous mandat, office → vendeur) | **autofacturation 389** : F15 + ADR-0022 mergés mais **zéro code** | [[autofacturation-389-axe-produit]] |
| 25 | 🚧 **Intracom au PRIX TOTAL** (262 ter / VIES / état 289 B) | BUG-11 **partiel** : signalé, non traité | recette §F |

---

## À décider avec Karl avant de seeder
- **Cases 3/4/5 (export)** : le dataset réel contient-il des BA `code_export` hors UE / CEE / franchise ? sinon injecter 1 cas par zone.
- **Case 7 (facture 5,5 %)** : injecter une facture au taux réduit pour exercer le second taux ordinaire.
- **Cases 16/20** : injecter 1 doc « pays inconnu » + 1 doc « ligne sans régime » pour prouver le fail-closed (sinon on ne teste que le libellé).
- **Gaps 23-25** : les laisser hors périmètre recette (documenter comme « connu, non codé »), pas les seeder comme attendus-verts.
