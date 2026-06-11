namespace Stratum.Modules.Job.Contracts.Services;

/// <summary>
/// Catalogue en lecture seule des types de jobs RÉELLEMENT enregistrés (un <c>IJobHandler&lt;T&gt;</c> existe
/// pour chacun). Alimente l'admin des planifications : la liste des types est FIXE (un type non enregistré ne
/// peut être ni sélectionné ni validé) et chaque type porte un libellé français destiné à l'opérateur — le
/// <c>FullName</c> .NET reste la clé technique stockée (<see cref="JobTypeDescriptor.TechnicalKey"/>), JAMAIS
/// affichée. Liakont addition (FIX211).
/// </summary>
public interface IJobTypeCatalog
{
    /// <summary>Tous les types de jobs enregistrés, triés par libellé.</summary>
    IReadOnlyList<JobTypeDescriptor> GetAll();

    /// <summary>Le descripteur d'un type par sa clé technique (FullName), ou <c>null</c> s'il n'est pas enregistré.</summary>
    JobTypeDescriptor? Find(string technicalKey);
}
