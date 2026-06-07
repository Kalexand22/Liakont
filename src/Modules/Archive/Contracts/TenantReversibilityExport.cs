namespace Liakont.Modules.Archive.Contracts;

using System.Collections.Generic;

/// <summary>
/// Dossier de RÉVERSIBILITÉ d'un tenant (F12 §6.3, décision 2026-06-03) : le dossier COMPLET que le client
/// emporte s'il quitte la plateforme. Réunit le suivi des documents (tracking), le coffre d'archive entier,
/// le paramétrage du tenant (profil, fiscal, comptes PA — secrets TOUJOURS masqués, table TVA, planification,
/// seuils) et le journal d'audit. Tenant-scopé. Consommé par API03 (<c>GET /api/v1/tenant-export</c>, permission
/// <c>liakont.settings</c>), qui le sérialise en archive téléchargeable. L'outillage opérateur de cette
/// réversibilité est OPS06 ; ici c'est la matière exportée.
/// </summary>
/// <param name="Files">Les fichiers du dossier (tracking, archive, paramétrage, journal, notice). Le rapport
/// d'intégrité du coffre figure dans la section <c>archive/</c>.</param>
/// <param name="Notice">La notice de réversibilité en français (également présente en fichier dans <see cref="Files"/>).</param>
public sealed record TenantReversibilityExport(
    IReadOnlyList<FiscalExportFile> Files,
    string Notice);
