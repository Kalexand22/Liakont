namespace Liakont.Agent.Core.Tests.Hosting;

using System;
using System.Threading;
using FluentAssertions;
using Liakont.Agent.Core.Hosting;
using Xunit;

/// <summary>
/// Verrou inter-process de sérialisation des runs (F12 §2.3) : un seul détenteur à la fois, libéré
/// proprement. Les tests utilisent un mutex à nom LOCAL unique (pas Global) pour éviter le privilège
/// SeCreateGlobalPrivilege et l'interférence entre tests ; la sémantique du verrou est identique.
/// </summary>
public class InterProcessRunLockTests
{
    [Fact]
    public void Acquire_release_then_reacquire_succeeds()
    {
        string name = UniqueName();

        InterProcessRunLock? first = InterProcessRunLock.TryAcquire(TimeSpan.FromSeconds(1), name);
        first.Should().NotBeNull();
        first!.Dispose();

        InterProcessRunLock? second = InterProcessRunLock.TryAcquire(TimeSpan.FromSeconds(1), name);
        second.Should().NotBeNull();
        second!.Dispose();
    }

    [Fact]
    public void Second_acquirer_is_blocked_while_the_lock_is_held()
    {
        string name = UniqueName();
        using (var acquired = new ManualResetEventSlim(false))
        using (var release = new ManualResetEventSlim(false))
        {
            InterProcessRunLock? held = null;
            var holder = new Thread(() =>
            {
                held = InterProcessRunLock.TryAcquire(TimeSpan.FromSeconds(1), name);
                acquired.Set();
                release.Wait();
                held?.Dispose();
            })
            {
                IsBackground = true,
            };
            holder.Start();

            acquired.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
            held.Should().NotBeNull("le thread détenteur a acquis le verrou");

            InterProcessRunLock? mine = InterProcessRunLock.TryAcquire(TimeSpan.FromMilliseconds(200), name);
            mine.Should().BeNull("le verrou est déjà détenu par un autre porteur");

            release.Set();
            holder.Join();

            InterProcessRunLock? after = InterProcessRunLock.TryAcquire(TimeSpan.FromSeconds(1), name);
            after.Should().NotBeNull("le verrou est libéré après la fin du détenteur");
            after!.Dispose();
        }
    }

    private static string UniqueName() => $@"Local\LiakontRunLockTest_{Guid.NewGuid():N}";
}
