namespace Liakont.Host.PaDelivery;

using System.IO;
using Liakont.Modules.Transmission.Contracts;
using Microsoft.Extensions.Logging;

/// <summary>
/// Canal de livraison par DÉPÔT DE FICHIER (F16 §6.2) implémentant <see cref="IDocumentDeliveryChannel"/>
/// au Host : écrit l'artefact (Factur-X scellé) dans le dossier / point de montage configuré PAR TENANT
/// (<see cref="DocumentDeliveryRequest.Target"/>). V1 = système de fichiers ; SFTP = fast-follow (package
/// SSH.NET + ADR dédié, F16 §6.2/§10). Le nom de fichier est assaini (aucun séparateur de chemin) pour
/// confiner l'écriture au dossier cible (jamais d'écriture hors du dossier du tenant).
/// </summary>
internal sealed partial class FileDepositDocumentDeliveryChannel : IDocumentDeliveryChannel
{
    private readonly ILogger<FileDepositDocumentDeliveryChannel> _logger;

    public FileDepositDocumentDeliveryChannel(ILogger<FileDepositDocumentDeliveryChannel> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public DocumentDeliveryMethod Method => DocumentDeliveryMethod.FileDeposit;

    /// <inheritdoc />
    public async Task DeliverAsync(DocumentDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Target);

        if (request.Content.IsEmpty)
        {
            // Bloquer plutôt que déposer un fichier vide (CLAUDE.md n°3).
            throw new InvalidOperationException(
                "Factur-X vide : dépôt de fichier bloqué (jamais d'écriture à vide).");
        }

        var destination = ResolveDestinationPath(request.Target, request.FileName);

        var folder = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        await File.WriteAllBytesAsync(destination, request.Content.ToArray(), cancellationToken).ConfigureAwait(false);

        LogDeposited(_logger, destination);
    }

    /// <summary>
    /// Calcule le chemin de destination : <paramref name="fileName"/> est réduit à son SEUL nom de fichier
    /// (aucun séparateur de chemin ni « .. ») et combiné au dossier cible du tenant — l'écriture reste
    /// confinée au dossier configuré (défense contre la traversée de répertoire). Pure pour être testable.
    /// </summary>
    internal static string ResolveDestinationPath(string targetFolder, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        // Path.GetFileName neutralise tout segment de chemin éventuel dans le nom de fichier reçu.
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            throw new InvalidOperationException(
                $"Nom de fichier de dépôt invalide (« {fileName} ») — dépôt bloqué.");
        }

        return Path.Combine(targetFolder, safeFileName);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Factur-X déposé : {Destination}.")]
    private static partial void LogDeposited(ILogger logger, string destination);
}
