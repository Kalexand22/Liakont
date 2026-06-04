# Invariants — module TvaMapping

| ID | Rule | Enforcement |
|---|---|---|
| INV-TVAMAPPING-001 | Une catégorie de TVA n'existe que dans la liste UNCL5305 sourcée F03 §2.1 (S, AA, AAA, Z, E, AE, G, K, O) ; aucune n'est inventée (CLAUDE.md n°2). | `VatCategory` (enum partagé Agent.Contracts) ; `VatCategoryParser.Parse` rejette tout code hors liste ; `MappingTableValidator` rejette une catégorie non définie au chargement. |
| INV-TVAMAPPING-002 | Une exonération à 0 % (catégorie E, taux fixe 0) exige un code VATEX (motif d'exonération) — F03 §2.2. | `MappingTableValidator.Validate` ajoute une violation ; `MappingTable.Create`/`Reconstitute` lèvent `InvalidMappingTableException`. |
| INV-TVAMAPPING-003 | Au plus une règle par couple (code régime source, part) — item TVA01 §3. Les flags source ne créent pas de 2ᵉ règle : ils restreignent la règle unique (non-correspondance → comportement par défaut `block`). | `MappingTableValidator` (HashSet sur (code, part), hors flags) + contrainte `uq_mapping_rules_regime_part` en base. |
| INV-TVAMAPPING-004 | Le taux est `decimal` exact, jamais flottant (CLAUDE.md n°1). Mode `Fixed` ⇒ valeur ≥ 0 présente ; mode `ComputedFromSource` ⇒ valeur absente. | Colonne `rate_value numeric` ; `MappingTableValidator.ValidateRate`. |
| INV-TVAMAPPING-005 | La validation structurelle s'applique à l'écriture ET au chargement ; une table invalide lève une exception (message opérateur français + action), jamais de comportement silencieux (CLAUDE.md n°3). | `MappingTable.Create` (écriture) et `MappingTable.Reconstitute` (chargement, via `TvaMappingMaterializer`) appellent `MappingTableValidator.EnsureValid`. |
| INV-TVAMAPPING-006 | Une table sans `validatedBy`/`validatedDate` est chargeable mais « NON VALIDÉE » ; cet état est exposé. | `MappingTable.IsValidated` (les deux champs requis) ; `MappingTableDto.IsValidated`. |
| INV-TVAMAPPING-007 | Régime non listé ⇒ comportement par défaut `block` (jamais d'envoi à l'aveugle) — F03 §4.1. | `MappingDefaultBehavior` (seule valeur `Block`) ; appliqué par le moteur TVA02. |
| INV-TVAMAPPING-008 | Toute lecture/écriture est scopée par `company_id` ; jamais de requête cross-tenant (CLAUDE.md n°9/17). | `WHERE company_id = @CompanyId` (UoW + queries + materializer) ; `uq_mapping_tables_company`. |
| INV-TVAMAPPING-009 | Aucune donnée client embarquée : les noms de flags source (ex. `RegimeMarge`) sont du paramétrage tenant, jamais codés dans le produit (CLAUDE.md n°7). | `SourceFlags` = dictionnaire générique `string → string` ; aucun nom de flag en dur dans le code. |
