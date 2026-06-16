# Module `SupportTrace` — trace de support du Factur-X transmis (FX06)

> Spec : [F16 §7](../../../docs/conception/F16-FacturX-Generation.md) · ADR-0023 · CLAUDE.md n°4/9/10.

## Objet

Conserver une **copie du Factur-X réellement transmis** à des fins de **support** (rejouer/diagnostiquer un
envoi), dans un **store dédié**, **tenant-scopé**, **chiffré au repos**, à **rétention courte** (proposition
90 jours, configurable — F16 §10) et **purgeable** (purge planifiée autorisée car NON-audit).

À **distinguer formellement** de :
- la **piste d'audit** `documents.document_events` (append-only, WORM, jamais purgée — module Documents) ;
- l'**archive probante** (coffre WORM, NF Z42-013, rétention 10 ans — module Archive, niveau Pilotage).

Le module n'a **aucune connaissance** de ces deux-là : sa purge ne peut donc pas les altérer (garde de
frontière, `SupportTraceBoundaryTests`).

## Surface

- `Contracts/` : `ISupportTraceStore` (Write/Read/PurgeOlderThan), `ISupportTracePurgeService`.
- `Infrastructure/` : `FileSystemSupportTraceStore` (chiffrement Data Protection par tenant, partition par
  jour de transmission), `SupportTraceOptions` (RootPath + RetentionDays), `SupportTracePurgeService`,
  `SupportTracePurgeTenantJob` + `SupportTracePurgeFanOutHandler` + `SupportTracePurgeTrigger` (fan-out SOL06),
  `SupportTraceModuleRegistration`.

## Câblage

Enregistré au Host par `AddSupportTraceModule` ; le handler de purge par `AddJobHandler<SupportTracePurgeTrigger, …>`.
La **planification** (cron) de la purge est un **geste opérateur** (la cadence relève du déploiement —
housekeeping d'une rétention courte, aucune cadence inventée), comme le digest de supervision.

L'**écriture** de la trace (au moment de la transmission) et la consommation pipeline sont câblées par **FX07**.
