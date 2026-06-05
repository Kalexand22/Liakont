namespace Liakont.Agent.Core.Hosting;

using System;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

/// <summary>
/// Verrou de sérialisation des RUNS d'extraction entre processus (F12 §2.3, AGT05) : le run planifié
/// (service Windows, LocalSystem) et le run manuel (CLI lancé par l'intégrateur) partagent un MÊME
/// mutex Windows nommé <see cref="DefaultMutexName"/>, pour ne jamais extraire/pousser deux fois en
/// parallèle sur le même poste.
/// <para>
/// EXIGENCE ACL : le mutex est créé dans l'espace de noms <c>Global\</c> avec un descripteur de
/// sécurité accordant Synchronize+Modify aux UTILISATEURS AUTHENTIFIÉS — sans quoi le CLI (compte
/// intégrateur) ne pourrait pas acquérir le mutex créé par le service (LocalSystem). La création d'un
/// objet <c>Global\</c> exige le privilège SeCreateGlobalPrivilege (détenu par LocalSystem et les
/// administrateurs) ; l'installeur (OPS05) garantit que le service tourne sous un tel compte.
/// </para>
/// </summary>
public sealed class InterProcessRunLock : IDisposable
{
    /// <summary>Nom du mutex partagé service/CLI (espace de noms Global, valable inter-sessions).</summary>
    public const string DefaultMutexName = @"Global\LiakontAgentRun";

    private readonly Mutex _mutex;
    private int _disposed;

    private InterProcessRunLock(Mutex mutex)
    {
        _mutex = mutex;
    }

    /// <summary>
    /// Tente d'acquérir le verrou avant <paramref name="timeout"/>. Renvoie le verrou tenu (à libérer
    /// par <see cref="Dispose"/>) ou <c>null</c> si un autre processus le détient déjà.
    /// </summary>
    public static InterProcessRunLock? TryAcquire(TimeSpan timeout, string? mutexName = null)
    {
        Mutex mutex = CreateOrOpen(mutexName ?? DefaultMutexName);
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(timeout, exitContext: false);
        }
        catch (AbandonedMutexException)
        {
            // Le détenteur précédent (run planté) n'a pas libéré le mutex : on le récupère proprement.
            acquired = true;
        }
        catch
        {
            mutex.Dispose();
            throw;
        }

        if (!acquired)
        {
            mutex.Dispose();
            return null;
        }

        return new InterProcessRunLock(mutex);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Libération depuis un thread non détenteur : ignoré (le handle est fermé juste après).
        }

        _mutex.Dispose();
    }

    private static Mutex CreateOrOpen(string name)
    {
        var security = new MutexSecurity();
        var authenticatedUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, domainSid: null);
        security.AddAccessRule(new MutexAccessRule(
            authenticatedUsers,
            MutexRights.Synchronize | MutexRights.Modify,
            AccessControlType.Allow));

        return new Mutex(initiallyOwned: false, name, out bool _, security);
    }
}
