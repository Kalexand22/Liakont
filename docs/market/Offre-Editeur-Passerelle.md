# Offre Éditeur — Le modèle « grappe » en détail

> Rédigé le 2026-06-02, à partir des DR7-DR17 et des arbitrages de la session de travail.
> Documents liés : `DR9-Business-Model-Pricing-Scenarios-CA.md` (pricing), `DR11-Analyse-Editeurs-Cibles.md` (cibles), `DR17-Strategie-Multi-PA-Partenaires.md` (PA et archivage), `SYNTHESE-DR-Commerciales.md`.

---

## 0. Vocabulaire

Le modèle est **générique** : il s'applique à tout éditeur de logiciel métier legacy. Le terme neutre est **« site déployé »** ou **« client final »**.

Le pitch, lui, se décline **dans le vocabulaire métier de chaque éditeur** — c'est précisément ce qui nous différencie du discours générique des intégrateurs EDI (Esalink, Tenor : « vos clients », « vos utilisateurs ») :

| Éditeur | Ses clients s'appellent |
|---|---|
| 3pi (CHEOPS), ISATECH (EncheresV6) | des **études** (de commissaires-priseurs) |
| Agisoft (Agimarée) | des **criées**, des **mareyeurs** |
| NSI-SADIMO (Vinéa) | des **domaines**, des **caves** |
| Autres verticaux | garages, officines, cabinets… |

---

## 1. Le problème de l'éditeur : il ne raisonne pas en gain, il raisonne en perte évitée

Un petit éditeur vertical (2-10 salariés) vit de la **maintenance de son parc** : chaque client lui paie ~2-5 k€/an. La réforme ne lui fait rien gagner — elle menace ce qu'il a.

| Scénario | Conséquence pour l'éditeur |
|---|---|
| **Il ne fait rien** | Sept. 2027 : ses clients DOIVENT être conformes. Son logiciel ne sait pas le faire → ils migrent vers un concurrent qui le sait. **Chaque client perdu = 2-5 k€/an de maintenance perdue, définitivement.** Sur un parc de 50 clients, c'est l'entreprise qui meurt. |
| **Il développe lui-même** | 50-100+ jours de dev (formats, API PA, e-reporting, annuaire, cycle de vie, veille AFNOR), un domaine qu'il ne connaît pas, un contrat PA à négocier. Irréaliste pour une équipe de 4. |
| **Il s'associe avec nous** | Zéro investissement, son parc est préservé, et il gagne un revenu nouveau (voir §3-4). |

**Le pitch en une phrase :**
> « Ça ne vous coûte rien, ça vous évite de perdre votre parc, et si vous déployez vous-même, ça vous rapporte 30-40 k€/an. »

Et l'argument de fermeture (à utiliser avec parcimonie) :
> « Si vous ne le proposez pas à vos clients, quelqu'un d'autre le fera — et ce quelqu'un deviendra leur prochain éditeur. »

---

## 2. Qui finance le connecteur ? Les 3 structures

Le « connecteur » (adaptateur) = les requêtes d'extraction + le mapping spécifiques au logiciel de l'éditeur. Le moteur générique (génération Factur-X/UBL, client PA, e-reporting, archivage) existe déjà — c'est notre produit, amorti sur tous les éditeurs.

### Structure A — « Zéro investissement éditeur » ⭐ recommandée pour les petits éditeurs

**Nous finançons le connecteur. L'éditeur ne sort pas un euro.**

| Qui | Apporte | Reçoit |
|---|---|---|
| **Nous** | Le développement de l'adaptateur (notre risque) | 70-80 % du setup et du récurrent de chaque site |
| **L'éditeur** | Accès au schéma de base + base de test + **distribution active** (mailing au parc, webinaire commun) + support N1 | 20-30 % de tout, **sans investissement ni risque** |

**Pourquoi c'est viable pour nous** : sur la même niche métier, l'adaptateur est largement réutilisable. Pour CHEOPS, le mapping métier enchères (bordereaux, régime de la marge, vendeurs/acheteurs, e-reporting B2C) est déjà fait pour EncheresV6 — seules les requêtes SQL changent : **5-10 jours**, pas 15-20. Point mort : **~8-12 sites déployés**. Sur un parc de 50+, le risque est faible.

### Structure B — « Premier client payeur » (le modèle CMP)

Le plus gros client du parc (ou le plus pressé) paie le connecteur comme projet sur-mesure (15-25 k€), puis le reste du parc en bénéficie en déploiement packagé. C'est exactement le rôle du CMP pour EncheresV6. **À utiliser quand un tel client existe** — sinon Structure A.

### Structure C — « L'éditeur co-investit » ❌ à oublier pour les petits éditeurs

L'éditeur paie 5-10 k€ et prend 40-50 % du récurrent. **Les petits éditeurs n'ont pas ce budget pour quelque chose qui ne leur fait pas gagner plus** — c'est un « non » immédiat qui grille le premier rendez-vous. À ne proposer qu'à des éditeurs établis (NSI-SADIMO éventuellement, groupes).

---

## 3. Les deux niveaux de partenariat

### Niveau 1 — « Distribution » (le démarrage)

L'éditeur distribue et fait le support N1, **nous déployons** chaque site.

- Revenu pour nous : ~900-1 400 € de setup + ~600-850 €/an par site
- Limite : **notre capacité** (~30-40 déploiements/an maximum en solo)
- Rôle : valider le modèle sur les 10-20 premiers sites, roder l'outillage

### Niveau 2 — « Partenaire certifié » (la solution au goulot) ⭐ l'objectif

**L'éditeur déploie lui-même** avec notre outillage, après une formation de 2-3 jours. Nous devenons fournisseur de logiciel + N2/N3.

| Qui | Fait | Touche (par site, palier Standard) |
|---|---|---|
| **L'éditeur** | Le déploiement (~2 j), le N1, facture SON client à SON prix (grille du §6 : ~49-59 €/mois + setup 500-900 €) | **~400-600 € de setup + ~290-390 €/an** — sa marge, son business |
| **Nous** | Le logiciel, le N2/N3, la veille réglementaire, la PA (marque grise Super PDP), l'archivage 10 ans | **~250-350 € de licence/site + ~280-330 €/an de gros** |

**Pourquoi c'est le bon montage :**
1. L'éditeur **gagne vraiment de l'argent** (40 sites équipés = ~20 k€ de setup + ~14 k€/an de récurrent) → il a un intérêt financier réel à pousser, pas seulement à « éviter de perdre »
2. **Notre capacité n'est plus le goulot** → le pic 2027 devient encaissable, le modèle peut se répliquer sur plusieurs éditeurs
3. L'éditeur connaît ses clients et son logiciel mieux que nous → déploiements plus rapides, moins d'escalades
4. L'exclusivité devient stable : un éditeur qui gagne de l'argent ne va pas voir ailleurs

**Trajectoire type avec un éditeur : Niveau 1 sur les 10-15 premiers sites (on rode, on documente, on outille), puis bascule en Niveau 2 avec formation de son équipe.**

---

## 4. Économie détaillée par site déployé

### Nos coûts réels par site

| Poste | Coût annuel |
|---|---|
| PA Super PDP absorbée (Offre Éco « PA incluse ») | ~40-60 € |
| Quote-part infra (serveurs, monitoring) | ~30-50 € |
| Archivage 10 ans | ~10 € |
| **Total cash** | **~80-120 €/an/site** |
| Support N2/N3 | ~1-2 h/an (si le produit est robuste) |
| Veille réglementaire | Mutualisée — pas un coût marginal |

### Marge par site selon le canal

| | **Niveau 1** (on déploie) | **Niveau 2** (l'éditeur déploie) | **Direct** (type CMP) |
|---|---|---|---|
| Setup — on encaisse | ~700-1 100 € | ~250-350 € (licence) | 5 000-35 000 € |
| Setup — notre temps | 2-3 jours | ~0,5 jour (validation) | 20-40 jours |
| Récurrent — on encaisse (gros, palier Standard) | ~450-600 €/an | ~280-330 €/an | 1 200-3 600 €/an |
| Récurrent — coût cash | ~80-100 €/an | ~80-100 €/an | ~50 €/an |
| **Marge récurrente nette** | **~370-500 €/an (≈ 80 %)** | **~200-230 €/an (≈ 70 %)** | **~1 150-3 550 €/an (≈ 95 %)** |

### Le rendement de notre temps (la métrique qui compte en solo)

Sur la durée de vie d'un client (rétention 5 ans, hypothèse DR9) :

| | Revenu total 5 ans | Notre temps total | **€/jour de notre temps** |
|---|---|---|---|
| **Niveau 2** | ~1 400-1 700 € | ~1 jour | **~1 500 €/jour** |
| **Niveau 1** | ~2 900-3 400 € | ~3,5 jours | **~900 €/jour** |
| **Direct** | ~25 000 € | ~40 jours | **~600 €/jour** |

**Les enseignements :**
1. **Le paradoxe central** : le client qui rapporte le moins en absolu (Niveau 2) est celui qui rémunère le mieux notre temps. Plus on délègue, plus chaque heure rapporte.
2. **Le direct n'est pas un modèle, c'est un financement** : 600 €/jour = un bon TJM de consultant, mais c'est du TJM, pas de la rente. Le CMP se justifie comme financeur de l'adaptateur (Structure B), pas comme modèle à répliquer.
3. **La marge en % est un faux sujet** (70-95 % partout). La vraie question : combien de clients peut-on empiler sans saturer notre temps ? Niveau 2 : des centaines. Niveau 1 : ~40/an. Direct : 2-3/an.
4. ⚠️ **La robustesse du produit est LE sujet économique** : ces marges supposent ~1-2 h de support/an/site. Si le produit est fragile (rejets fréquents, données sources sales) et que ça monte à 1-2 jours/an/site, tout s'effondre. À ~200 €/an net par site, **une seule journée de support consomme la marge de 2-3 sites** — l'investissement dans la qualité (validation amont, monitoring proactif, auto-correction) n'est pas du confort technique, c'est ce qui protège la marge.

### La cible à 3 ans (recalculée : grille de prix corrigée + pénétration 90 %)

Avec un net de ~200-230 €/an par site et **90 % de pénétration des parcs** (le parc d'un éditeur équipé est captif — voir §6) :

| Grappe | Parc estimé | Sites équipés (90 %) | Notre récurrent net/an |
|---|---|---|---|
| 3pi (CHEOPS) | ~60 | ~54 | ~11 k€ |
| ISATECH (EncheresV6) | ~100 | ~90 | ~19 k€ |
| Grappe 3 (ex. Agimarée — criées) | ~50 | ~45 | ~9 k€ |
| Grappes 4-5 (autres verticaux) | ~80 | ~72 | ~15 k€ |
| **Total (~5 grappes)** | **~290** | **~260** | **~54 k€/an** + setups/licences |

**⏰ La contrainte de calendrier est absolue (correction utilisateur)** : les clients des éditeurs doivent être conformes en **septembre 2027**. Donc déployés à l'été 2027 → adaptateur prêt au printemps 2027 → **signature de l'éditeur au plus tard T1 2027**. Un éditeur signé en 2028 ne sert à rien : son parc aura trouvé une autre solution ou sera en train de se disperser.

**Le cycle de signature d'un éditeur est court : ~3 semaines** (correction utilisateur). Le décideur est le patron (pas de comité), l'urgence est existentielle, l'offre est sans risque ni investissement. Un appel, une démo, un contrat. **Le goulot n'est donc pas le cycle de vente — c'est d'avoir les munitions prêtes** le jour où on décroche le téléphone :

| Munition à préparer | Pourquoi elle conditionne les « 3 semaines » | Prête au plus tard |
|---|---|---|
| **Une démo qui tourne** (CMP ou pilote 3pi) | Sans preuve, 3 semaines deviennent 3 mois (confiance à construire) | T4 2026 |
| **Un contrat type validé par avocat** (action OE-4) | S'il part en allers-retours juridiques, le délai explose. Doit être signable tel quel | T3-T4 2026 |
| **Le one-pager chiffré** (tableau du §8 décliné dans le vocabulaire métier de la cible) | Le patron doit pouvoir décider sur une page | T4 2026 |
| **La liste des éditeurs candidats qualifiés** (grappes 3-5) | 1-2 semaines de travail (NAF 5829C + annuaires fédérations métier) | T3 2026 |

Le rétroplanning est une **campagne**, pas un pipeline :

| Échéance | Ce qui doit être fait |
|---|---|
| **Été 2026** | CMP/ISATECH + industrialisation produit. **3pi signable dès maintenant** (le travail CMP suffit comme preuve — même métier) |
| **T3-T4 2026** | Préparer les munitions (contrat type, one-pager, liste candidats) + pilote 3pi qui tourne |
| **T4 2026 → T1 2027** | **La campagne de signatures** : grappes 3-4-5 contactées, signées en ~3 semaines chacune, en parallèle |
| **S1 2027** | Adaptateurs 3-4-5 (5-10 j chacun) + formations Niveau 2 |
| **S2 2027** | Les éditeurs déploient en masse (Niveau 2) — nous : N2/N3 + supervision |
| **2028+** | Plus de nouvelles signatures liées à la réforme — uniquement du récurrent, des retardataires et le marché de renouvellement |

**Le métier en 2026-2027 : signer et équiper des éditeurs. Pas déployer des sites.** Notre temps de 2027 se planifie en conséquence : ~30-50 jours pour les adaptateurs et formations, le reste en N2/N3 et supervision — pas en déploiements.

---

## 5. Le support : qui fait quoi

| Niveau | Qui | Contenu | Volume |
|---|---|---|---|
| **N1 — Usage** | **Éditeur** | « Comment je fais ? », statuts, formation utilisateurs. Le client appelle sa hotline habituelle (marque blanche : il ne sait pas que nous existons) | ~80 % des tickets |
| **N2 — Technique** | **Nous** | Rejets PA, statuts non remontés, erreurs de mapping, données sources incohérentes | ~15-20 % |
| **N3 — PA / réglementaire** | **Nous + escalade PA** | Pannes API, évolutions de format, questions fiscales | ~1-5 % |

**Prérequis pour que ça tienne** (à construire avant le premier déploiement de grappe) :
1. **Tableau de bord de statuts** accessible au support de l'éditeur — sinon son N1 escalade tout
2. **FAQ / base de connaissances** + formation du support éditeur (~½ journée)
3. **Matrice d'escalade contractuelle** : périmètre N1/N2, SLA, canal (ticket, pas téléphone)
4. **Monitoring proactif** : nos alertes détectent les rejets avant l'appel du client

⚠️ **Vérification à faire au premier contact avec chaque éditeur** : son support a-t-il la capacité d'absorber le N1 ? (« Combien de personnes au support ? Quel volume de tickets aujourd'hui ? ») Un N1 défaillant chez l'éditeur, et les clients finissent par nous trouver et nous appeler.

---

## 6. Ce que voit le client final : l'Offre Éco « PA incluse »

> ⚠️ **Correction du 2026-06-02** : la première version visait ~100-120 €/mois. Trop cher pour le bas du marché — une étude de 5 personnes compare ça à sa maintenance logicielle (+30-50 %), à son expert-comptable (« il me le fera peut-être ») et à l'alternative gratuite (saisie manuelle dans le portail d'une PA). **La concurrence du bas du marché n'est pas Esalink : c'est la flemme et le portail gratuit.**

Le client final reçoit une offre simple, dans la marque de son éditeur, **avec des paliers par volume** :

| Palier | Profil | Prix client/mois | Notre prix de gros/an |
|---|---|---|---|
| **Micro** | 1-3 pers., < 1 000 bordereaux/an | **29 €** | ~180 € |
| **Standard** | 5-10 pers., 1 000-5 000 bordereaux/an | **49-59 €** | ~280-330 € |
| **Grand compte** | > 5 000 bordereaux/an, multi-sites | **89-119 €** | ~450-550 € |
| Setup | — | 500-900 € (ou 0 € avec engagement 24 mois) | licence ~250-350 € |

Tout est inclus à chaque palier : émission, réception, e-reporting (B2B international, B2C, paiements), PA (Super PDP marque grise, ~3-5 €/mois absorbés), archivage 10 ans, veille réglementaire. **UN contrat (avec l'éditeur), zéro démarche PA.**

**Le pitch client à ce prix n'est plus « la conformité » (subie) mais le temps gagné :**

> « Pour 49 €/mois, vos bordereaux partent tout seuls — vous ne saisissez plus rien, ni pour vos acheteurs pros, ni pour les particuliers, ni pour les impôts. »

C'est le prix de 2-3 heures de saisie évitées par mois — un calcul que même la plus petite structure fait instantanément.

**Le parc est captif : la pénétration attendue est ~90 %** (correction utilisateur du 2026-06-02) :

- La « concurrence » de la saisie manuelle n'existe pas en réalité : une étude qui sort 200 bordereaux par vente ne resaisira jamais à la main, et les portails « gratuits » des PA factureront de toute façon un montant comparable à terme.
- Quand l'éditeur propose une solution intégrée à prix correct (dans la fourchette du marché), **presque tout le parc actif s'équipe**. Les seuls non-équipés : les structures qui ferment ou qui avaient déjà décidé de migrer.
- Le rôle du prix bas n'est donc pas de « créer » l'adoption (elle est quasi acquise) mais : ① d'être **acceptable** pour les petites structures (pas de résistance, pas de ressentiment), ② de **verrouiller le parc** (aucun concurrent ne peut venir proposer moins cher derrière nous), ③ de **maximiser la marge de l'éditeur** sur le volume (il pousse fort).

| Parc de 60 sites, pénétration 90 % | Résultat |
|---|---|
| Sites équipés | ~54 |
| Notre récurrent net/an sur ce parc | **~11 k€/an** |
| Récurrent éditeur/an | **~18 k€/an** |
| Setup cumulé (nous / éditeur) | ~16 k€ / ~27 k€ |

---

## 7. Ce qu'on exige de l'éditeur (non monétaire mais non négociable)

| Exigence | Pourquoi |
|---|---|
| **Accès au schéma de base + base de test** | Sans ça, le coût de l'adaptateur triple (reverse engineering en aveugle) |
| **Exclusivité 2-3 ans** sur la conformité de son parc | Protège notre investissement (Structure A) ; stable si l'éditeur gagne de l'argent (Niveau 2) |
| **Communication conjointe au parc** (mailing, webinaire) | Un mail de l'éditeur convertit 10× mieux que notre prospection à froid |
| **Support N1** | Condition de viabilité du modèle (voir §5) |
| **Un interlocuteur technique désigné** | Pour le schéma, les cas particuliers, la formation Niveau 2 |

---

## 8. Pitch 3pi prêt à l'emploi (à décliner pour chaque éditeur)

### L'accroche
> « CHEOPS ne sait pas produire de Factur-X, et vos études devront être conformes en septembre 2027. On a développé la solution pour EncheresV6 [crédibilité : même métier, Crédit Municipal de Paris]. On vous propose de l'adapter à CHEOPS — ça ne vous coûte rien, et ça vous rapporte. »

### Le tableau à montrer (Niveau 2, parc estimé ~60 études, grille 29/49-59/89-119 €/mois, pénétration ~90 %)

| | 2027 | 2028 | 2029 |
|---|---|---|---|
| Études équipées (cumul — le parc est captif, personne ne resaisira à la main) | 40 | 50 | 54 |
| **Revenu 3pi** (~500 € de marge setup + ~340 €/an/étude) | **~20 k€ + 14 k€/an** | ~5 k€ + 17 k€/an | ~2 k€ + **18 k€/an** |
| **Investissement 3pi** | **0 €** | 0 € | 0 € |
| Parc préservé (maintenance conservée) | ~60 × 3 k€ = **180 k€/an sécurisés** | idem | idem |

> Lecture pour 3pi : **~27 k€ de setup cumulé + un récurrent qui monte à ~18 k€/an**, sans investir un euro ni embaucher. Pour une structure de 4 personnes, c'est plus qu'un demi-salaire chargé qui tombe tout seul — et surtout, les 180 k€/an de maintenance existante sont sécurisés.
>
> ⏰ **L'urgence à mettre en face** : pour que les 40 premières études soient déployées avant sept. 2027, l'accord doit être signé **avant fin 2026** (adaptateur au T1 2027, formation au T2, déploiements été 2027). Chaque mois de retard = des études qui cherchent ailleurs.

### Le déroulé proposé
1. **Maintenant** : accord de principe + accès au schéma CHEOPS + base de test → on développe l'adaptateur (5-10 jours, à notre charge)
2. **T4 2026** : démo sur données réelles + communication conjointe au parc (webinaire « CHEOPS et la réforme 2027 »)
3. **T1 2027** : 10-15 premiers déploiements faits par nous (Niveau 1) + formation de votre technicien
4. **À partir de T2 2027** : vous déployez vous-même (Niveau 2), on assure le N2/N3 et la veille réglementaire

### Les objections probables et les réponses

| Objection | Réponse |
|---|---|
| « On va peut-être développer ça nous-mêmes » | « C'est 50-100 jours de dev sur un domaine mouvant (les normes AFNOR évoluent encore), plus un contrat PA à négocier. Pendant ce temps, vos concurrents démarchent vos études. Et on l'a déjà fait. » |
| « Pourquoi on vous laisserait accéder à notre schéma ? » | « NDA + l'adaptateur ne fonctionne qu'avec notre moteur. Votre schéma ne sort jamais de la mission. Et c'est vous qui gardez la relation client. » |
| « Et si vous disparaissez ? » | « Clause de réversibilité : le code de l'adaptateur est mis sous séquestre / licence source à votre profit en cas de défaillance de notre part. Et l'archivage de vos clients est restituable. » |
| « Nos études ne paieront jamais ça » | « À partir de 29 €/mois pour les petites, 49-59 €/mois pour une étude standard — c'est le prix de 2-3 heures de saisie évitées par mois. L'alternative, c'est saisir chaque bordereau à la main dans un portail, ou 50 € d'amende par facture non conforme. » |

---

## 9. Ce qui est atteignable / pas atteignable

| | Verdict |
|---|---|
| Structure A avec 3pi (on finance, ils distribuent) | ✅ **Atteignable** — l'offre la plus facile à accepter : zéro risque pour eux |
| Bascule au Niveau 2 dès 2027 | ✅ **Atteignable et souhaitable** — 3pi a des techniciens qui connaissent CHEOPS et ses clients |
| Faire payer le connecteur à un petit éditeur (Structure C) | ❌ **Pas atteignable** — pas le budget, « non » immédiat |
| Exclusivité sans contrepartie financière durable | ⚠️ **Fragile** — l'exclusivité tient si l'éditeur gagne de l'argent (Niveau 2) |
| Répliquer sur ISATECH | ⚠️ **Dépend de l'issue du RJ** (≈ 7 juillet 2026) — même schéma : Structure B (le CMP paie) + Niveau 2 ensuite. L'offre CMP passe NÉCESSAIREMENT par ISATECH (autorisation + probablement exécution) |
| Répliquer sur les grappes 3-4-5 (Agimarée, viticulture, autre) | ✅ **Atteignable SI signées au plus tard T1 2027** — après, c'est trop tard (leurs clients doivent être déployés avant sept. 2027). Le Niveau 2 rend la capacité non bloquante ; le goulot est le nombre de signatures, pas les déploiements |
| 100 % du parc d'un éditeur | Réaliste : **~90 %** du parc actif (le parc est captif : personne ne resaisit à la main, les alternatives coûtent pareil). Les ~10 % restants : structures qui ferment ou migraient déjà |

---

## 10. Risques et clauses contractuelles clés

| Risque | Parade contractuelle |
|---|---|
| L'éditeur signe puis ne pousse pas (distribution molle) | Clause d'objectif : si < N déploiements à date D, l'exclusivité tombe (on peut démarcher son parc en direct) |
| L'éditeur veut récupérer l'adaptateur et nous évincer | L'adaptateur ne fonctionne qu'avec notre moteur (architecture) + propriété intellectuelle claire au contrat |
| L'éditeur coule (cf. ISATECH) | Le contrat de chaque site déployé est tripartite ou cessible : si l'éditeur disparaît, la relation bascule en direct avec nous |
| Le N1 de l'éditeur est défaillant | SLA contractuel + droit de re-facturer le support hors périmètre |
| Notre défaillance (on est seul) | Séquestre du code / licence de survie au profit de l'éditeur — c'est aussi un argument de vente (§8, objections) |

---

## 11. Actions

| # | Action | Quand |
|---|---|---|
| OE-1 | Valider ce modèle d'offre (relecture / ajustements) | Maintenant |
| OE-2 | Intégrer le tableau de bord de statuts + le monitoring proactif dans le backlog produit (prérequis Niveau 1) | Conception |
| OE-3 | Préparer le support de pitch 3pi (slides ou one-pager à partir du §8) | Quand la démo CMP est montrable |
| OE-4 | Faire valider les clauses clés (§10) par un avocat (exclusivité, séquestre, cessibilité) | Avant signature 3pi |
| OE-5 | Vérifier la santé financière et la taille réelle du parc 3pi (Pappers + appel SYMEV) | Avant le premier contact |
