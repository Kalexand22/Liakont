namespace Liakont.Host.Dashboard;

/// <summary>État de la table de mapping TVA tel qu'affiché sur le tableau de bord d'accueil.</summary>
public enum DashboardTvaStatus
{
    /// <summary>Aucune table TVA paramétrée pour ce tenant.</summary>
    NotConfigured,

    /// <summary>Table paramétrée mais NON VALIDÉE par l'expert-comptable (envois suspendus).</summary>
    NotValidated,

    /// <summary>Table validée humainement.</summary>
    Validated,
}
