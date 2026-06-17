# Client soft de signature sur place — `clients/OnSiteSignature`

Capteur **desktop-companion** de signature manuscrite **sur place** (salle des ventes / criée), pour le
volet « sur place » du module Signature (**ADR-0030**, **F17 §6**). Racine de solution **distincte**, **jamais
sous `agent/`** : frontière physique + isolation du SDK Wacom natif (INV-ONSITE-1).

## Frontière (non négociable)

- **Pur capteur, aucune logique métier.** Le client capte (geste + horodatage + binding hash), chiffre la FSS
  localement et **POST un objet immuable** au proxy plateforme `OnSiteCapture` (HTTPS, auth derrière
  l'abstraction IdP, tenant-scopé). **Toute** décision (transition de validation, ouverture de gate) reste
  côté plateforme (INV-ONSITE-4).
- **Ne référence NI `Liakont.Agent.*` NI un module plateforme.** Il parle au proxy **uniquement en HTTP**
  (aucune `ProjectReference` vers un contrat plateforme). Garanti par `OnSiteSignatureClientBoundaryTests`
  (liste blanche fermée par jeton de clé publique Microsoft + `Newtonsoft.Json`).
- **Secrets : DPAPI LOCALE au client** (`ProtectedData`, `DataProtectionScope.CurrentUser` + entropie
  applicative — `LocalDpapiSecretProtector`), **jamais** le `ISecretProtector` de l'agent (INV-ONSITE-9).
  `CurrentUser` (et non `LocalMachine`) : appli interactive mono-session sur un **poste de criée partagé** →
  moindre privilège (un autre compte Windows du poste ne déchiffre pas).
- **RGPD sobre** : on capte le tracé comme **preuve d'intégrité/consentement** ; **aucun gabarit /
  feature-vector** n'est dérivé de la FSS (INV-ONSITE-10).

## Hash de binding (ADR-0030 §4)

`BindingHasher` calcule le **SHA-256 des octets EXACTS** de l'artefact Factur-X scellé reçu (sans
re-canonicalisation). Le client signe ce hash ; la plateforme re-hashe son artefact stocké et vérifie
`re-hash == hash signé` (**même flux d'octets** client/plateforme — INV-ONSITE-6).

## Dépendances de déploiement à intégrer via un ADR de package (NON référencées dans le source buildé)

Pour préserver le **build** et le **test de pureté** (le SDK Wacom natif ne doit pas entrer dans l'assembly,
INV-ONSITE-2/3), ces dépendances **natives/de déploiement** ne sont **pas** des `PackageReference` de ce
projet — elles sont intégrées à l'**installeur du poste** (hôte exécutable) derrière l'abstraction
`ISignaturePadDevice` :

- **Wacom Ink SDK for signature** (pad STU, USB, .NET Framework) — capture réelle de la FSS. À inventorier
  par un **ADR de package** dédié avant intégration (Post-Dev Checklist, CLAUDE.md).
- **Lib PAdES/CAdES** de scellement (si le scellement côté poste est requis) — à inventorier de même.

Le présent projet fournit la **logique testable** (capture orchestrée, binding, DPAPI locale, POST HTTPS) et
l'abstraction `ISignaturePadDevice` ; l'**hôte exécutable** du poste (intégrant le SDK Wacom) est livré par
l'installeur de déploiement, qui fournit l'implémentation concrète du pad.
