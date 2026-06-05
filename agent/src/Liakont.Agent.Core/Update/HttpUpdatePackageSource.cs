namespace Liakont.Agent.Core.Update;

using System;
using System.IO;
using System.Net;
using System.Net.Http;

/// <summary>
/// Source de mise à jour sur <see cref="HttpClient"/> (net48, TLS 1.2+, HTTPS sortant). Mince couche de
/// transport : elle télécharge le manifeste (en mémoire) et le paquet (vers un fichier), sans politique.
/// Un échec réseau/HTTP ne lève jamais — la décision appartient au coordinateur.
/// </summary>
public sealed class HttpUpdatePackageSource : IUpdatePackageSource, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    /// <summary>Crée une source HTTPS. Force TLS 1.2 (poste client net48 ancien possible).</summary>
    /// <param name="handler">Gestionnaire HTTP injectable (tests d'intégration) ; par défaut <see cref="HttpClientHandler"/>.</param>
    /// <param name="timeout">Délai d'expiration des téléchargements (défaut 5 min — un paquet peut être gros).</param>
    public HttpUpdatePackageSource(HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        EnsureTls12();
        _http = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _ownsClient = true;
        _http.Timeout = timeout ?? TimeSpan.FromMinutes(5);
    }

    /// <inheritdoc/>
    public byte[]? TryDownloadManifest(string manifestUrl)
    {
        if (string.IsNullOrWhiteSpace(manifestUrl))
        {
            return null;
        }

        try
        {
            using (HttpResponseMessage response = _http.GetAsync(manifestUrl, HttpCompletionOption.ResponseContentRead).GetAwaiter().GetResult())
            {
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                return response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            }
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            // Délai d'expiration (HttpClient.Timeout) — traité comme un échec de téléchargement.
            return null;
        }
    }

    /// <inheritdoc/>
    public bool TryDownloadPackage(string packageUrl, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(packageUrl) || string.IsNullOrWhiteSpace(destinationPath))
        {
            return false;
        }

        try
        {
            using (HttpResponseMessage response = _http.GetAsync(packageUrl, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
            {
                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                using (Stream source = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                using (var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    source.CopyTo(destination);
                }

                return true;
            }
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            // Délai d'expiration (HttpClient.Timeout) — traité comme un échec de téléchargement.
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }

    private static void EnsureTls12()
    {
        // net48 peut négocier des protocoles anciens par défaut selon la configuration machine ;
        // on garantit au minimum TLS 1.2 pour le canal sortant (F12 §3.1).
        if ((ServicePointManager.SecurityProtocol & SecurityProtocolType.Tls12) == 0)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }
    }
}
