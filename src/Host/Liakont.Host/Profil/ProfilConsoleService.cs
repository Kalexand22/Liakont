namespace Liakont.Host.Profil;

using System;
using System.Threading;
using System.Threading.Tasks;
using Liakont.Modules.TenantSettings.Contracts.Commands;
using Liakont.Modules.TenantSettings.Contracts.Queries;
using MediatR;
using Stratum.Common.Abstractions.Exceptions;

/// <summary>
/// Implémentation de <see cref="IProfilConsoleService"/> (BUG-15). LECTURE : profil du tenant courant via
/// <c>GetTenantProfileQuery</c> (tenant résolu côté handler). MUTATION : <c>SaveTenantProfileCommand</c>, avec
/// le SIREN REPRIS du profil persisté (immuable, INV-TENANTSETTINGS-001) — l'écran ne le saisit jamais, ce
/// chemin ne peut donc pas le changer, quelle que soit la requête. Aucune logique métier ici (CLAUDE.md n°19) :
/// seule normalisation de présentation = chaîne d'e-mail vide → <c>null</c>. Tenant-scopé (CLAUDE.md n°9).
/// </summary>
internal sealed class ProfilConsoleService : IProfilConsoleService
{
    private readonly ISender _sender;

    public ProfilConsoleService(ISender sender)
    {
        _sender = sender;
    }

    public async Task<ProfilViewModel?> GetAsync(CancellationToken cancellationToken = default)
    {
        var profile = await _sender.Send(new GetTenantProfileQuery(), cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return null;
        }

        return new ProfilViewModel
        {
            Siren = profile.Siren,
            Form = new ProfilFormModel
            {
                RaisonSociale = profile.RaisonSociale,
                Street = profile.Street,
                PostalCode = profile.PostalCode,
                City = profile.City,
                Country = profile.Country,
                ContactEmailAlerte = profile.ContactEmailAlerte,
            },
        };
    }

    public async Task SaveAsync(ProfilInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Le SIREN (clé fonctionnelle immuable) est REPRIS du profil persisté : ce chemin ne peut pas le
        // modifier, quelle que soit la saisie côté client. Sans profil existant, l'édition n'a pas lieu d'être.
        var current = await _sender.Send(new GetTenantProfileQuery(), cancellationToken).ConfigureAwait(false)
            ?? throw new ConflictException(
                "Le profil du tenant n'existe pas encore : créez-le avant de le modifier.");

        var command = new SaveTenantProfileCommand
        {
            Siren = current.Siren,
            RaisonSociale = input.RaisonSociale,
            Street = input.Street,
            PostalCode = input.PostalCode,
            City = input.City,
            Country = input.Country,
            ContactEmailAlerte = NullIfBlank(input.ContactEmailAlerte),
        };

        await _sender.Send(command, cancellationToken).ConfigureAwait(false);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
