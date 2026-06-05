namespace Liakont.Agent.Adapters.EncheresV6.Tests.Fakes;

using System.Data.Common;

/// <summary>
/// Exception base de données concrète pour tester la traduction des défaillances ODBC en
/// <c>SourceUnavailableException</c> (le PervasiveExtractor capture <see cref="DbException"/>).
/// </summary>
internal sealed class FakeDbException : DbException
{
    public FakeDbException(string message)
        : base(message)
    {
    }
}
