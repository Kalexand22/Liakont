# ADR-0018 — Transport SMTP réel des notifications (MailKit) pour les alertes de supervision

- **Statut** : Proposé (2026-06-08).
- **Date** : 2026-06-08
- **Contexte décisionnel** : `orchestration/items/SUP.yaml` (SUP03), `docs/conception/F12-Architecture-Plateforme-Agent.md`
  §5.3 (destinataires) et §6.1 (config SMTP au niveau instance), `CLAUDE.md` n°5 (tout nouveau package = ADR ;
  cf. `blueprint.md` §5), `CLAUDE.md` n°10/18 (secrets — mot de passe SMTP jamais en clair),
  `CLAUDE.md` n°11/20 (socle `Stratum.*` vendored non modifié), `docs/architecture/provenance-socle-stratum.md`
  (le transport email du socle est un stub), `src/Modules/Notification/Contracts/IEmailTransport.cs`.

## Contexte

Le module **Notification** du socle Stratum (vendored) fournit le pipeline complet de notification
(templates, file d'attente de jobs, retry, événements `email.sent`/`email.failed`), mais son **seul**
transport est `StubEmailTransport` qui se contente de journaliser — aucun email ne part réellement, et
**aucun package SMTP** n'est présent dans `Directory.Packages.props` (constat review 2026-06-03,
consigné dans le profil de provenance du socle).

SUP03 (supervision proactive, F12 §5) doit envoyer par email les alertes critiques/avertissements
(agent muet, documents bloqués, rejets PA) à l'opérateur d'instance et, optionnellement, au contact
d'alerte du tenant. Cela exige un **transport SMTP réel**, branché sur l'abstraction `IEmailTransport`
du socle, consommée par `EmailSendJobHandler` au moment de la livraison.

Le runtime .NET 10 n'embarque **aucun** client SMTP recommandé : `System.Net.Mail.SmtpClient` est
explicitement déconseillé par Microsoft (pas de support STARTTLS moderne, marqué « obsolète pour de
nouveaux usages », recommandation officielle : MailKit). Il faut donc un package tiers.

## Décision

### 1. Dépendance : MailKit (+ sa dépendance MimeKit)

On ajoute **MailKit 4.17.0** (et sa dépendance verrouillée **MimeKit 4.17.0**) en gestion centralisée
(`Directory.Packages.props`). MailKit est le client SMTP recommandé par Microsoft à la place de
`System.Net.Mail.SmtpClient` ; il est maintenu, largement déployé, supporte STARTTLS/SSL et
l'authentification, et MimeKit compose un message MIME correct (encodage UTF-8 des sujets/corps en
français accentué — exigence produit, `CLAUDE.md` n°12).

### 2. Périmètre : Host uniquement, sur l'abstraction vendored (socle NON modifié)

Le transport SMTP est un **détail d'infrastructure d'instance**. On implémente un `SmtpEmailTransport`
(classe **Liakont**, interne au `Host`) qui implémente `Stratum.Modules.Notification.Contracts.IEmailTransport`,
et on l'enregistre au **composition root** (`AppBootstrap`) **en remplacement** du `StubEmailTransport`
(via `services.Replace(...)`). **Aucun fichier `Stratum.*` vendored n'est modifié** (pas d'entrée de
provenance requise) : on consomme une abstraction du socle, on n'en altère pas l'implémentation. Le
`StubEmailTransport` reste le transport des tests (les tests surchargent `IEmailTransport`).

La dépendance MailKit/MimeKit est donc référencée **par le seul projet `Liakont.Host`** — aucun module
métier, aucun module du socle ne la voit. Le module **Supervision** ne référence PAS MailKit : il
compose le contenu de l'alerte et enfile un `EmailSendJobPayload` (Contracts), point.

### 3. Configuration SMTP = niveau instance, secret jamais en clair

Les paramètres SMTP (hôte, port, identifiants, expéditeur, STARTTLS) sont une configuration de
**niveau instance** (F12 §6.1) : liés par `IOptions<SmtpOptions>` depuis la section `Smtp` des
`appsettings`. Le `appsettings.json` versionné ne porte qu'un **gabarit vide** (mot de passe `""`),
exactement comme `Keycloak.ClientSecret`/`AdminSeed.Password` ; les valeurs réelles sont injectées au
déploiement par variables d'environnement / `appsettings.Production.json` non versionné (jamais en
clair dans le dépôt — `CLAUDE.md` n°10). Le mot de passe SMTP n'est **jamais journalisé** (le transport
ne logue que destinataire + sujet, jamais les identifiants — `CLAUDE.md` n°18).

### 4. Échec d'envoi non bloquant

Transport **non configuré / désactivé** (`Smtp.Enabled=false` ou hôte vide) : le transport journalise
un avertissement et **ne lève pas** (pas de retry infini sur une instance sans SMTP — les alertes
restent visibles au dashboard). Transport **configuré mais échec d'envoi** : il **lève**, et le
`EmailSendJobHandler` du socle retente le job (jusqu'à `MaxRetries`) sans jamais interrompre
l'évaluation des règles (qui ne fait qu'**enfiler** un job, jamais d'envoi synchrone — SUP03).

## Invariants

- **INV-SUP03-1** — La dépendance MailKit/MimeKit n'est référencée que par `Liakont.Host`. Aucun module
  métier ni module du socle ne la référence.
- **INV-SUP03-2** — Aucun fichier `Stratum.*` vendored n'est modifié : `SmtpEmailTransport` est une classe
  Liakont implémentant l'abstraction `IEmailTransport`, enregistrée au composition root.
- **INV-SUP03-3** — Le mot de passe SMTP n'apparaît jamais en clair dans un fichier versionné ni dans un
  log. `appsettings.json` ne porte qu'un gabarit vide ; les valeurs réelles viennent de l'environnement.
- **INV-SUP03-4** — L'échec d'envoi SMTP ne casse jamais l'évaluation des règles : l'évaluation enfile un
  job ; l'envoi réel (et son éventuel retry) est porté par le pipeline de jobs du module Notification.

## Conséquences

**Positif** : les alertes partent réellement par email ; on réutilise intégralement le pipeline du socle
(file, retry, événements) sans le modifier ; la dépendance tierce est confinée au Host ; la config et le
secret suivent le pattern d'instance déjà en place (Keycloak/AdminSeed). Le transport en mode
« désactivé » se comporte comme le stub (log), donc une instance sans SMTP démarre et fonctionne (les
alertes restent au dashboard) sans erreur.

**À accepter** : une dépendance tierce de plus à suivre (MailKit/MimeKit, même auteur, cadence de
release régulière). Le test d'envoi SMTP réel (connexion à un serveur) relève d'une vérification de
déploiement / d'un test d'intégration dédié, pas du test unitaire : on teste unitairement la
**composition du message** (destinataire/sujet/corps/UTF-8) et le mode désactivé, et on mocke
`IEmailTransport` pour le flux alerte→email.

**Limite** : MailKit cible `net8.0` et tourne sur `net10.0` ; pas de spécificité .NET 10 requise.

## Alternatives rejetées

- **`System.Net.Mail.SmtpClient` (in-box, sans nouveau package, sans ADR)** : déconseillé par Microsoft
  (pas de STARTTLS moderne, marqué obsolète pour de nouveaux usages, doc officielle pointe MailKit).
  Contredit aussi l'instruction explicite de SUP03 (« MailKit ou équivalent + ADR »). **Rejetée.**
- **Modifier `StubEmailTransport` pour y mettre l'envoi SMTP** : touche un fichier `Stratum.*` vendored
  (provenance, `CLAUDE.md` n°11/20) pour un besoin couvert par un enregistrement DI au composition root.
  **Rejetée** (inutile et plus risqué).
- **Mettre le transport SMTP dans le module Supervision** : mêle un détail de transport d'instance à un
  module métier, et y ferait entrer le secret SMTP. Le transport est un concern d'instance (Host) ;
  Supervision ne fait que composer et enfiler. **Rejetée.**

## Références

- `orchestration/items/SUP.yaml` (SUP03) ; `docs/conception/F12-Architecture-Plateforme-Agent.md` §5.3, §6.1
- `src/Modules/Notification/Contracts/IEmailTransport.cs` ; `src/Modules/Notification/Infrastructure/Handlers/Jobs/EmailSendJobHandler.cs`
- `CLAUDE.md` n°5 (package = ADR), n°10/18 (secrets), n°11/20 (socle vendored) ; `blueprint.md` §5
- `docs/architecture/provenance-socle-stratum.md` (transport email = stub)
