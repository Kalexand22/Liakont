# DR16 — Offre « réception » comme produit d'appel

> Deep research exécutée le 2026-06-02 (99 agents).
> Plan parent : `Plan-DR-Marche-Commercial.md`.
> Solidité : 3 claims confirmés (cadre réglementaire et sanctions), le reste en abstention technique. Les volets « besoin d'intégration réel » et « offres/prix du marché » restent ouverts.

---

## Verdict synthétique

1. **L'obligation de réception est réelle et sanctionnée — mais modestement.** Sanction graduée et distincte de celle d'émission : mise en demeure de 3 mois → amende de **500 €** → puis **1 000 €** renouvelable tous les 3 mois en cas de persistance. C'est une pression faible comparée aux sanctions d'émission (50 €/facture, plafond 15 000 €/an).
2. **L'offre « réception » seule est difficile à vendre** : la sanction est trop faible pour créer l'urgence, et la conformité minimale (désigner une PA, consulter son portail) est quasi gratuite.
3. **MAIS la logique de produit d'appel est confirmée par le calendrier** : toute entreprise doit choisir une PA pour la réception dès sept. 2026 → c'est le moment où le contact commercial se noue. Le prestataire qui aide à la mise en place de la réception en 2026 est naturellement positionné pour l'émission obligatoire de sept. 2027.

**Recommandation** : ne pas développer de produit « réception » autonome. Intégrer un volet « réception » léger (aide au choix de PA + paramétrage annuaire) dans l'offre globale, comme **prestation d'entrée à faible prix** (500-1 500 €) qui crée la relation client avant la vraie vente (émission 2027).

---

## 1. Constats CONFIRMÉS

### Calendrier ✅ 3-0

| Obligation | Date | Qui |
|---|---|---|
| **Réception** | 1er sept. 2026 | **Toutes** les entreprises assujetties TVA |
| Émission + e-reporting | 1er sept. 2026 | GE + ETI |
| Émission + e-reporting | 1er sept. 2027 | PME/TPE/micro |

Le report proposé au printemps 2025 (décalage 2027-2028) a été **explicitement rejeté** par l'Assemblée nationale en avril 2025. Dates définitives.

### Sanction spécifique au défaut de réception ✅ 2-0

> Source primaire : entreprendre.service-public.gouv.fr/actualites/A18802 + Fiducial

| Étape | Sanction |
|---|---|
| 1. Constat de non-conformité (pas de PA désignée pour la réception) | **Mise en demeure** — délai de 3 mois |
| 2. Non-conformité persistante après mise en demeure | **Amende de 500 €** |
| 3. Persistance | **1 000 €**, renouvelable **tous les 3 mois** |
| Tolérance | Première infraction réparée spontanément ou sous 30 jours |

**Lecture commerciale** : la sanction réception est graduée, plafonnée de fait à ~4 000 €/an, et conditionnée à une mise en demeure préalable. La DGFiP cherche l'adhésion, pas la répression. → **L'argument de peur ne fonctionne pas pour la réception.** L'argument qui fonctionne : « vos fournisseurs vont vous envoyer des factures électroniques que vous ne saurez pas traiter, et votre comptable va devenir fou ».

## 2. Questions restées OUVERTES (non confirmées par la recherche)

Ces points conditionnent le design de l'offre et nécessitent une validation terrain (pas une DR de plus — le sujet est épuisé en ligne) :

1. **Routage en l'absence de PA destinataire** : où arrive concrètement la facture du fournisseur si le destinataire n'a pas de PA ? (rejet ? statut « non transmise » ? blocage annuaire ?) → à clarifier avec B2Brouter ou la doc DGFiP.
2. **Le portail web de la PA suffit-il opérationnellement ?** Pour une TPE avec 10 factures fournisseurs/mois, oui. Pour une PME avec 200 factures/mois et un workflow de validation, non — c'est là que l'intégration dans le SI legacy redevient un sujet. **La frontière du « besoin réel » est le volume de factures fournisseurs.**
3. **Prix de marché des solutions de réception** : non établis (claims réfutés ou abstenus).
4. **Taux de conversion réception → émission** : aucune donnée. C'est le pari du produit d'appel.

## 3. Design d'offre recommandé

| Composante | Contenu | Prix indicatif | Rôle |
|---|---|---|---|
| **« Pack conformité réception »** (2026) | Aide au choix de PA + inscription annuaire + paramétrage du compte + formation 1h consultation portail | **500–1 500 € HT** one-shot | Produit d'appel — crée la relation |
| Option « intégration réception » | Injection des factures fournisseurs dans le SI legacy (extraction du portail PA → écriture comptable) | Sur devis (2-5 k€) | Pour les PME à fort volume fournisseurs uniquement |
| **Suivi → bascule émission** (2027) | Le client réception 2026 devient prospect chaud émission 2027 | Pricing DR9 | La vraie vente |

**Argument de vente du pack réception** : « pour 1 000 €, vous êtes en règle pour septembre 2026, et vous avez un partenaire qui connaît déjà votre système pour l'échéance de 2027 qui est, elle, beaucoup plus lourde. »

## 4. Lien avec le produit (impact conception)

- Le Pack réception ne nécessite **aucun développement** dans Gateway.Core : c'est de la prestation de paramétrage (compte PA, annuaire). → Vendable dès maintenant, avant même que le produit soit fini.
- L'option « intégration réception » nécessiterait un module d'**écriture** dans le SI source — contraire au principe « lecture seule stricte » du produit (`Conception-Produit-Passerelle.md` §3). → À ne PAS développer en V1 ; si la demande se confirme, ce serait un produit séparé avec ses propres règles de prudence.

## 5. Actions découlant de cette DR

| # | Action | Priorité |
|---|---|---|
| DR16-A1 | Clarifier avec B2Brouter le routage des factures vers un destinataire sans PA (question support, lien avec A2 du README Conception) | 🟠 Moyenne |
| DR16-A2 | Tester le « Pack réception » sur le CMP lui-même (ils doivent aussi recevoir dès sept. 2026 — sont-ils prêts ?) | 🟠 Moyenne |
| DR16-A3 | Inclure le Pack réception dans la grille tarifaire (DR9) et le site web (DR15, page /guides/calendrier) | 🟡 Basse |

## 6. Données techniques

- **Stats** : 99 agents, 25 claims vérifiés → 3 confirmés (calendrier 3-0, sanctions réception 2-0 ×2), 22 abstentions/réfutations techniques.
- **Sources primaires** : entreprendre.service-public.gouv.fr (A18802), economie.gouv.fr, fiducial.fr, cegid.com.
- **Volets non couverts** : besoin d'intégration réel (b), offres/prix du marché (c), pricing prestataire (d) — sujets à valider par le terrain plutôt que par une nouvelle DR.
