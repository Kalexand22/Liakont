namespace Liakont.Modules.DocumentApproval.Domain;

using System.Linq;
using Liakont.Modules.DocumentApproval.Domain.Entities;
using Liakont.Modules.Signature.Contracts;

/// <summary>
/// RÈGLE DE GATE générique (ADR-0028 §5, INV-APPROVAL-4) — fonction PURE. Un gate de purpose ouvre pour un
/// document ssi, sur sa tentative la plus récente, les <b>trois</b> conditions sont réunies :
/// <list type="number">
/// <item>l'état ∈ <c>{Validated, TacitlyValidated}</c> (NÉCESSAIRE, non suffisante) ;</item>
/// <item>la preuve attachée satisfait le niveau requis CONFIGURÉ par le tenant (PAR slot en N-parties ; un
/// <c>Recorded</c> nu ne franchit pas une exigence SES/AES/QES ; une bascule tacite ne satisfait que
/// <c>Recorded</c>) ;</item>
/// <item>pour le purpose self-billing et UNIQUEMENT sur une transition <c>Validated</c> EXPRESSE : une
/// acceptation enregistrée explicite (la condition 3 NE s'applique PAS à <c>TacitlyValidated</c>).</item>
/// </list>
/// Le niveau requis vient du CHOIX du tenant (paramétrage câblé par les ports de purpose, SIG06), jamais d'une
/// obligation produit. Le gate n'est ni durci ni affaibli au nom d'une obligation inexistante (CLAUDE.md n°2/3).
/// </summary>
public static class ApprovalGate
{
    /// <summary>
    /// Statue sur l'émissibilité d'un document au regard de sa <paramref name="validation"/> et du
    /// <paramref name="requiredLevel"/> exigé par le tenant pour ce purpose. LECTURE pure (aucune mutation).
    /// </summary>
    public static ApprovalGateDecision Evaluate(DocumentValidation validation, SignatureLevel requiredLevel)
    {
        ArgumentNullException.ThrowIfNull(validation);

        // Condition 1 — NÉCESSAIRE : état ∈ {Validated, TacitlyValidated}.
        if (validation.State is not (ValidationState.Validated or ValidationState.TacitlyValidated))
        {
            return Closed(
                $"l'état de validation « {validation.State} » n'ouvre pas le gate " +
                "(attendu : Validated ou TacitlyValidated).");
        }

        // Condition 2 — niveau de preuve ≥ exigence tenant (PAR slot en N-parties).
        var policy = ValidationPurposePolicy.For(validation.Purpose);
        if (policy.UsesSlots)
        {
            if (validation.Slots.Count == 0 || validation.Slots.Any(s => !s.IsApproved))
            {
                return Closed("au moins un slot d'approbation n'est pas rempli (complétude N-parties non atteinte).");
            }

            var under = validation.Slots.FirstOrDefault(s => !SignatureLevelAssurance.Satisfies(s.ProofLevel, requiredLevel));
            if (under is not null)
            {
                return Closed(
                    $"le slot « {under.SignerId} » porte une preuve « {under.ProofLevel} » inférieure au niveau " +
                    $"requis « {requiredLevel} » (évaluation PAR slot — un slot sous-niveau n'ouvre pas le gate).");
            }
        }
        else
        {
            // Une bascule tacite (sans preuve rattachée) ne satisfait que Recorded (ADR-0028 §5 cond. 2).
            var effectiveProof = validation.State == ValidationState.TacitlyValidated
                ? SignatureLevel.Recorded
                : validation.ProofLevel;

            if (!SignatureLevelAssurance.Satisfies(effectiveProof, requiredLevel))
            {
                return Closed(
                    $"la preuve attachée « {effectiveProof} » est inférieure au niveau requis « {requiredLevel} » " +
                    "(un Recorded nu ne franchit pas une exigence SES/AES/QES).");
            }
        }

        // Condition 3 — self-billing, sur transition Validated EXPRESSE uniquement : acceptation enregistrée
        // explicite. NON appliquée à TacitlyValidated (régie par ses propres gardes — ADR-0028 §5).
        if (policy.RequiresExpressAcceptanceForm
            && validation.State == ValidationState.Validated
            && !validation.ExpressAcceptanceRecorded)
        {
            return Closed(
                "acceptation expresse non enregistrée : la validation expresse d'un document à acceptation " +
                "requise exige une acceptation explicite tracée (forme configurée par le tenant).");
        }

        return new ApprovalGateDecision
        {
            IsOpen = true,
            Reason = "Gate ouvert : état, niveau de preuve et forme d'acceptation satisfont la Règle de gate (ADR-0028 §5).",
        };
    }

    private static ApprovalGateDecision Closed(string reason)
        => new() { IsOpen = false, Reason = $"Émission bloquée — {reason}" };
}
