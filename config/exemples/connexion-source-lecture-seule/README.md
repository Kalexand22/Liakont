# Connexion à la base SOURCE en lecture seule — exemples par moteur (fictifs)

> Source : redline ADR fondateurs, finding **RL-AGT-6** (verrous de lecture implicites du moteur).
> Item d'orchestration : **RDF16**. Exemples **fictifs** (CLAUDE.md n°7) : aucune chaîne de
> connexion d'un client réel ici — la vraie chaîne est du **paramétrage de tenant chiffré DPAPI**
> (ADP04), jamais versionnée.

## 1. Ce que la lecture seule garantit déjà (prouvé)

L'agent **n'écrit jamais** sur la base source (CLAUDE.md n°5). La garantie produit est dans le
code, pas seulement déclarée :

- **Garde de préfixe SELECT** : toute requête passe par `EnsureSelectOnly`
  ([`SourceQueryGuard`](../../../agent/src/Liakont.Agent.Core/Extraction/SourceQueryGuard.cs) côté
  cœur, [`EncheresV6Schema.EnsureSelectOnly`](../../../agent/src/Liakont.Agent.Adapters.EncheresV6/Source/EncheresV6Schema.cs)
  côté adaptateur) → `INSERT/UPDATE/DELETE` impossibles.
- **Aucune transaction ni verrou explicite** : prouvé par test —
  [`RecordingConnection.BeginTransaction`](../../../agent/tests/Liakont.Agent.Adapters.EncheresV6.Tests/Fakes/RecordingConnection.cs)
  lève `NotSupportedException` et `TransactionsBegun == 0` est asservi
  ([`PervasiveExtractorTests`](../../../agent/tests/Liakont.Agent.Adapters.EncheresV6.Tests/PervasiveExtractorTests.cs)).

## 2. Le résidu : les verrous PARTAGÉS IMPLICITES du moteur

Même un `SELECT` sans transaction ni verrou explicite prend, sous l'**isolation par défaut
`READ COMMITTED`** de la plupart des moteurs, des **verrous partagés implicites** (shared locks) le
temps de la lecture. Ils ne violent pas la lecture seule (aucune écriture), mais ils peuvent
**ralentir** ou **bloquer brièvement** les écritures du logiciel métier source pendant l'extraction.

**Supprimer ces verrous implicites relève du PARAMÉTRAGE TENANT, pas du code.** La fabrique de
connexion **n'invente aucun attribut de pilote** et **transmet la chaîne telle quelle** — voir la
xml-doc de
[`OdbcEncheresV6ConnectionFactory`](../../../agent/src/Liakont.Agent.Adapters.EncheresV6/OdbcEncheresV6ConnectionFactory.cs)
(« Si le pilote configuré expose un attribut *read-only*, il est ajouté à la chaîne par la
configuration — défense en profondeur dépendante du pilote »). L'attribut exact dépend du **moteur**
et de la **version du pilote ODBC** ; les exemples ci-dessous sont des **points de départ à valider
contre la doc du pilote installé chez le client**, pas des valeurs garanties.

## 3. Exemples par moteur (valeurs fictives)

### Microsoft SQL Server (ODBC Driver 17/18)

Deux leviers, combinables :

- **Read-only routing** au niveau connexion : `ApplicationIntent=ReadOnly` (route vers un réplica
  lisible si un groupe de disponibilité existe ; sinon neutre).
- **Isolation snapshot** (supprime les verrous partagés de lecture) : à activer **côté base**
  (`ALTER DATABASE ... SET READ_COMMITTED_SNAPSHOT ON`), décision de l'**administrateur de la base
  source**, pas de l'agent.

```
# Chaîne ODBC d'EXEMPLE (fictive) — SQL Server
Driver={ODBC Driver 18 for SQL Server};Server=SERVEUR_FICTIF;Database=BASE_FICTIVE;Trusted_Connection=Yes;ApplicationIntent=ReadOnly;Encrypt=Yes;
```

### Actian Zen / Pervasive PSQL (moteur EncheresV6)

Le pilote ODBC Zen/Pervasive expose un **mode d'ouverture en lecture seule** dont le **nom exact de
l'attribut varie selon la version** du pilote (Btrieve vs Relational, v13/v14/v15). **Ne pas
deviner** : se reporter à la doc du pilote installé et ajouter l'attribut read-only fourni par
celle-ci. L'extraction reste de toute façon `SELECT` seul (garde §1).

```
# Chaîne ODBC d'EXEMPLE (fictive) — Actian Zen / Pervasive
# <ATTRIBUT_READONLY_DU_PILOTE> = à remplacer par l'attribut documenté de la version installée.
DSN=DSN_FICTIF_ENCHERESV6;<ATTRIBUT_READONLY_DU_PILOTE>=1;
```

### Moteur générique (autre pilote ODBC)

Si le pilote n'expose **aucun** attribut read-only, la lecture seule reste garantie par la garde
SELECT + l'absence de transaction (§1) ; la suppression des verrous partagés implicites n'est alors
**pas** atteignable par la chaîne et relève d'un réglage **côté serveur source** (niveau d'isolation
par défaut), décision de l'administrateur de cette base.

```
# Chaîne ODBC d'EXEMPLE (fictive) — pilote générique sans attribut read-only
DSN=DSN_FICTIF_SOURCE;
```

## 4. Frontière (rappel)

L'agent **ne paramètre rien tout seul** : la chaîne de connexion (avec ou sans attribut read-only)
est fournie par le **paramétrage de tenant chiffré** (ADP04), jamais en clair, jamais dans le code.
Ce dossier ne contient que des **exemples fictifs** pour documenter l'option par moteur.
