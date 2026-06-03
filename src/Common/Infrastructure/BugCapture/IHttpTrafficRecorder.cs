namespace Stratum.Common.Infrastructure.BugCapture;

public interface IHttpTrafficRecorder
{
    IReadOnlyList<HttpTrafficRecord> GetSnapshot(DateTimeOffset since);
}
