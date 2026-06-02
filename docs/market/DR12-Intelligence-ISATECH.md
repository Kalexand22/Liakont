# DR12 — Intelligence ciblée ISATECH

> Deep research exécutée le 2026-06-02 (100 agents, 19 sources, 84 claims extraits, 25 vérifiés).
> Plan parent : `Plan-DR-Marche-Commercial.md`.
> ⚠️⚠️ **AVERTISSEMENT MÉTHODOLOGIQUE MAJEUR** : la vérification adversariale a techniquement échoué sur 100 % des claims (votes 0-0 = abstention des agents vérificateurs, défaillance technique). **AUCUNE information de ce document n'est donc « confirmée » au sens de la méthode** — mais aucune n'est réfutée non plus. Les informations proviennent de sources primaires cohérentes entre elles (site isatech.fr, Pappers, societe.com, presse régionale). **Vérification manuelle indispensable avant toute décision** — en particulier le point §1.

---

## ⚡ Information critique n°1 — ISATECH EST en redressement judiciaire ✅ VÉRIFIÉ

> **✅ CONFIRMÉ manuellement le 2026-06-02** (vérification croisée Pappers + recherche BODACC) :
> **ISATECH (SIREN 326 862 570, Vannes) a été placée en redressement judiciaire par jugement du Tribunal de commerce de Rennes du 7 janvier 2026** :
> - date de cessation des paiements (provisoire) : **15 décembre 2025**
> - période d'observation courant jusqu'au **7 juillet 2026**
> - juge commissaire : Christine Robin
> - administrateur judiciaire : **SELAS AJIRE (Me Erwan Merly)**
> - mandataire judiciaire : SELARL GOPMJ (Me Pauline Collin)
> - publication : **BODACC A n°20260013, annonce n°2920** (21 janvier 2026)
>
> Dernier CA public connu : 14,7 M€ (exercice 2018), résultat net 556 k€. SAS au capital de 400 k€, créée le 26/04/1983, NAF 6202A, 100-199 salariés (donnée 2023).
>
> **Événement supplémentaire découvert lors de la vérification** : ISATECH a réalisé une **cession de branche d'activité à AD ULTIMA FRANCE le 24 juin 2025** (AD Ultima = groupe belge partenaire Microsoft Dynamics). Lecture probable : la branche Dynamics — le cœur moderne et valorisable — a été cédée AVANT le dépôt de bilan, ce qui laisse dans ISATECH les activités résiduelles (dont probablement le legacy Enchères SVV). À approfondir : qu'est-ce qui reste exactement dans le périmètre d'ISATECH en RJ ?

**Cette information change TOUT le calcul stratégique** (voir §6, scénario A désormais actif).

---

## 2. Structure du groupe (claims non vérifiés, sources primaires concordantes)

- ISATECH ferait partie du **groupe Dimood** (rebranding du groupe Isatech, ambition 40 M€ de CA, recrutements annoncés de 30-50 collaborateurs — presse régionale bretonne).
- **Innexa**, présenté comme l'éditeur du logiciel « Enchères SVV », aurait fusionné avec Isatech et été rebaptisé « **Isatech Enchères** ».
- Au sein du groupe, le développement logiciel des solutions enchères serait confié à **Bricklead** (entité distincte), Isatech assurant l'implémentation, le support et la qualité.
- Une holding distincte existe : HOLDING ISATECH (SIREN 404 408 692).

## 3. Le produit enchères et son parc (claims non vérifiés)

| Information | Détail | Source |
|---|---|---|
| Ancienneté | « Enchères SVV » = plus de **35 ans** d'expérience éditeur (cohérent avec la génération EncheresV6 / Magic XPA) | isatech.fr, innexa.fr |
| **Taille du parc** | **~100 sociétés de ventes clientes, plus de 700 utilisateurs** | isatech.fr |
| Successeur | **« Bricklead Enchères »** (SaaS) — présenté comme le successeur destiné à remplacer Enchères SVV | isatech.fr, bricklead.eu |
| Clients SVV identifiables | **Ader, SADDE, Magnin & Wedry, Olivier Doutrebente, Rouillac, Auction Art** ; témoignage détaillé : **Maison Chativesle** (Reims, Me Alban Gillet, 7 salariés) | isatech.fr |

> Recoupement interne : la liste de clients (Ader notamment) recoupe les bases hébergées sur le serveur `azmut-enbase01` (ADER, BATTIN, CHAUSSON, CORTOT…) mentionnées dans `Conception-Produit-Passerelle.md` §2. Cohérence forte entre l'intelligence externe et ce qu'on observe en interne.

## 4. Stratégie facturation électronique d'ISATECH (claims non vérifiés)

**Confirmation de notre hypothèse interne : ISATECH n'a RIEN annoncé pour EncheresV6 / Enchères SVV.**

- La stratégie de conformité d'ISATECH serait entièrement construite autour de **Microsoft Dynamics** :
  - **Continia Document Capture** (réception/OCR) + **Continia Document Output** (émission), formats Factur-X/UBL/CII ;
  - PA native de Continia : **Digital Technologies** (incluse dans la licence Continia) ;
  - couverture : Business Central SaaS (prêt avril 2026), BC v26+ on-premise, **NAV 2013 → BC25 via Continia à partir de mai 2026** ;
  - **NAV 2009 et antérieurs + autres systèmes legacy : NON couverts** — renvoyés vers « des solutions externes ou la PA choisie ».
- La page produit « Enchères SVV » ne mentionne **ni facturation électronique, ni PA/PDP, ni échéances 2026/2027**.
- Webinaire(s) facturation électronique : oui, mais centrés Dynamics/Continia.
- Partenaire e-invoicing listé : **Tungsten** (plateforme de facturation électronique).

## 5. Signaux R&D / recrutement (claims non vérifiés)

- Recrutement d'un **Tech Lead Microsoft Dynamics 365 Finance & Operations** (X++) — poste publié 27/05/2024, depuis pourvu.
- **Aucune offre d'emploi mentionnant Magic XPA ni la facturation électronique.**
- Lecture : les ressources R&D d'ISATECH sont orientées Dynamics 365 ; le legacy Magic XPA n'est pas une priorité d'investissement.

## 6. Analyse stratégique (mon analyse, croisant DR12 et le contexte interne)

### Scénario A — Le redressement judiciaire est confirmé

| Dimension | Implication |
|---|---|
| **Capacité d'ISATECH à développer un module EncheresV6** | Quasi nulle : une entreprise en RJ ne lance pas de chantier R&D sur un produit legacy en fin de vie. Notre hypothèse « ISATECH ne livrera rien avant 2027 » devient « ISATECH ne livrera probablement jamais ». |
| **Le parc des ~100 études SVV** | Orphelin de fait pour la conformité. Elles devront trouver une solution externe → **la Passerelle est cette solution**. |
| **Rapport de force du partenariat** | Inversé en notre faveur : ISATECH (ou son administrateur) a besoin de solutions à proposer à son parc pour conserver la valeur du fonds de commerce. Une passerelle fonctionnelle clé en main est un actif pour eux. |
| **Risque** | La maintenance d'EncheresV6 elle-même est en péril → les études pourraient être forcées de migrer (vers Bricklead Enchères ou un concurrent) plus vite que prévu, réduisant la durée de vie du marché. MAIS une migration de 100 études avant sept. 2027 reste matériellement impossible. |
| **Stratégie CMP** | ⚠️ **L'offre CMP passe nécessairement par ISATECH** : EncheresV6 est leur logiciel, leur autorisation est requise au minimum, et le projet sera probablement exécuté via eux. Le RJ ne permet pas de les contourner — il change seulement la posture : **relancer activement** (interlocuteur potentiellement ralenti par le RJ) plutôt qu'attendre passivement leur retour. |
| **Interlocuteur de négociation** | Ce n'est plus ISATECH mais potentiellement l'administrateur judiciaire (Selas Ajire) ou le repreneur. Le calendrier de la période d'observation (jusqu'au 7 juillet 2026) est un jalon clé. |

### Ce que le RJ implique — et ce qu'il n'implique PAS

Un redressement judiciaire n'est **pas une faillite**. L'entreprise continue d'opérer pendant la période d'observation, les contrats de maintenance restent en vigueur, les ~100 études ont leur éditeur et leurs engagements contractuels. Le parc n'est pas orphelin.

Ce que le RJ change réellement pour nous :

| Dimension | Implication nuancée |
|---|---|
| **Capacité R&D** | Réduite par les licenciements économiques. Un chantier R&D neuf sur EncheresV6 (module conformité) est peu probable dans ce contexte — mais c'était déjà notre hypothèse de départ. Rien de nouveau là-dessus. |
| **Les ~100 études clientes** | Elles ont toujours leur éditeur et leur maintenance. Ce ne sont pas des orphelins. Ce sont des clients d'un éditeur fragilisé, avec une **solution de conformité non annoncée** pour leur produit. |
| **Issue à surveiller** | Trois scénarios possibles fin juillet 2026 (BODACC) : plan de continuation (ISATECH restructurée), cession (un repreneur reprend l'activité), ou liquidation. Ce dernier cas seulement créerait un problème de continuité de service. |
| **Rapport de force** | Une passerelle fonctionnelle clé en main représente de la valeur pour la continuation ou un repreneur : ça rassure les études clientes sur la conformité 2027. Argument à avoir en réserve. |

**Ce qui reste vrai et inchangé** : ISATECH n'a rien annoncé pour EncheresV6, leur R&D est sur Dynamics/Continia, Bricklead Enchères SaaS ne peut pas absorber ~100 études d'ici sept. 2027. La Passerelle reste la solution la plus réaliste pour ce parc — mais ISATECH reste l'interlocuteur probable, dans un état financier fragilisé.

## 7. Actions découlant de cette DR

| # | Action | Priorité | Délai |
|---|---|---|---|
| ~~DR12-A1~~ | ~~Vérifier le redressement judiciaire~~ → **✅ FAIT le 2026-06-02 : CONFIRMÉ** (TC Rennes, 07/01/2026, BODACC A n°20260013) | ✅ | — |
| DR12-A2 | **Relancer ISATECH activement sur le dossier CMP** : l'offre passe nécessairement par eux (autorisation sur leur logiciel, probablement exécution du projet). Ne pas attendre passivement leur retour — une entreprise en RJ a un processus de décision ralenti, c'est à nous de pousser. | 🔴 **Critique** | Immédiat |
| DR12-A3 | Clarifier le périmètre de la cession à AD ULTIMA FRANCE (24/06/2025) : qu'est-ce qui reste dans ISATECH ? Où est logé Enchères SVV (ISATECH ? Innexa ? Bricklead ?) | 🔴 Haute | Juin 2026 |
| DR12-A4 | Vérifier l'existence et le périmètre réel de « Bricklead Enchères » (bricklead.eu) — calendrier, prix, clients pilotes, et sa santé (Bricklead est-elle dans le périmètre du RJ ?) | 🟠 Moyenne | Juin 2026 |
| DR12-A5 | **Surveiller le BODACC autour du 7 juillet 2026** (fin de période d'observation) : plan de continuation, cession, liquidation ? L'issue détermine l'interlocuteur de négociation. | 🔴 Haute | Début juillet 2026 |
| DR12-A6 | Constituer la liste des ~100 études du parc (croiser les clients nommés + les bases azmut-enbase01) comme fichier de prospection directe — plan de secours si la voie partenariat ISATECH reste bloquée | 🟡 Basse | Phase prospection |

## 8. Données techniques de la recherche

- **Stats** : 5 angles, 19 sources, 84 claims extraits, 25 vérifiés → **0 confirmé, 25 abstentions techniques** (aucune réfutation sur le fond).
- **Sources principales** : isatech.fr (pages produit, articles, webinaires, partenaires — primaires), pappers.fr, societe.com, innexa.fr, bricklead.eu, presse régionale bretonne (bretagne-economique.com, solutions-numeriques.com), site carrière Dimood.
- **Cohérence interne** : les claims se recoupent entre eux et avec notre connaissance interne (RECAP, bases azmut) — ce qui renforce leur plausibilité malgré l'absence de vérification formelle.
