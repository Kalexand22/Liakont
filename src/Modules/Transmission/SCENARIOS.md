# Scénarios de test — module Transmission

## Unit (`Liakont.Modules.Transmission.Tests.Unit`)

### Capacités déclarées et flux (INV-TRANSMISSION-001, 006)
- `PaCapabilitiesTests.SupportsPaymentReport_DistinguishesDomesticAndInternationalFlux` — les flux
  10.4 (domestique) et 10.2 (international) sont deux capacités séparées ; `SupportsPaymentReport`
  renvoie indépendamment selon le flux.

### Capacité absente = résultat typé journalisable (INV-TRANSMISSION-002)
- `SendPaymentReportAsync_UnsupportedFlux_ReturnsTypedCapabilityGap_NoException` — flux non supporté →
  `PaSendState.CapabilityNotSupported`, `CapabilityNotSupported` renseigné (capacité typée), aucun
  identifiant émis, aucune exception.
- `SendPaymentReportAsync_SupportedFlux_IsIssued` — flux supporté → `Issued`, pas de capacité absente.
- `GetGeneratedDocumentAsync_WhenRetrievalUnsupported_ReturnsTypedGap_NoContent_NoException` —
  téléchargement non supporté → capacité absente typée, contenu nul, aucune exception.
- `CapabilityNotSupportedResult_OperatorMessage_IsFrench_AndJournalisable` — message opérateur en
  français portant le nom de PA et le libellé de capacité.

### Résolution par registre de types (INV-TRANSMISSION-003, 004)
- `Resolve_KnownType_ReturnsClientFromFactory` — type connu → client de la fabrique ; le compte est
  propagé à la fabrique.
- `Resolve_IsCaseInsensitiveOnPaType` — la clé de type est insensible à la casse.
- `Resolve_UnknownType_Throws_WithFrenchMessage_NeverReturnsNull` — type inconnu → exception
  française listant les plug-ins disponibles (jamais `null`).
- `EmptyRegistry_Resolve_Throws_AucunDisponible` — registre vide → exception « aucun » disponible.
- `Constructor_DuplicateType_Throws` — deux fabriques du même type → exception au démarrage.
- `IsRegistered_And_RegisteredTypes_ReflectFactories` — diagnostic du registre cohérent.
- `AddTransmissionModule_RegistersRegistry_ThatResolvesRegisteredFactories` — enregistrement DI :
  `IPaClientRegistry` résolu via le conteneur découvre les `IPaClientFactory` enregistrées (prouve le
  câblage `Liakont.Host`).

### Frontières (INV-TRANSMISSION-005)
- `TransmissionBoundaryTests.Abstraction_HasNoHttpType` — aucun type HTTP dans l'abstraction (NetArchTest).
- `Contracts_DoesNotReferenceAnyConcretePaPlugin` / `Infrastructure_DoesNotReferenceAnyConcretePaPlugin`
  — aucune référence à `Liakont.PaClients.*`.
- `Contracts_DoesNotReachIntoAnotherBusinessModule` — seule dépendance inter-projet : le contrat agent
  partagé.

## Integration

Aucun en PAA01 : l'abstraction n'a ni base ni I/O. La **suite de contrat** rejouée contre chaque
plug-in arrive en PAA03 (contre le plug-in Fake livré par PAA02) ; les envois réels (staging
B2Brouter / sandbox Super PDP) restent **manuels, hors CI** (testing-strategy §8).
