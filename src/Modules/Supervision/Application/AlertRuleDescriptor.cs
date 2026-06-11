namespace Liakont.Modules.Supervision.Application;

using Liakont.Modules.Supervision.Domain;

/// <summary>Métadonnée d'une règle d'alerte du dispositif (F12 §5.2) : clé stable, libellé opérateur français,
/// gravité et nature du seuil. La règle est ACTIVE si une <see cref="IAlertRule"/> de même <see cref="RuleKey"/>
/// est enregistrée (SUP01b) ; sinon elle est GELÉE (SUP01c, producteur de données manquant) et reste affichée
/// « pour ne pas laisser croire à une couverture ».</summary>
/// <param name="RuleKey">Clé technique stable de la règle (alignée sur <see cref="IAlertRule.RuleKey"/> pour les règles actives).</param>
/// <param name="DisplayName">Libellé opérateur français (F12 §5.2).</param>
/// <param name="Severity">Gravité produit (F12 §5.2).</param>
/// <param name="ThresholdKind">Nature du seuil restitué.</param>
public sealed record AlertRuleDescriptor(
    string RuleKey,
    string DisplayName,
    AlertSeverity Severity,
    AlertRuleThresholdKind ThresholdKind);
