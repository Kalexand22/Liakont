namespace Stratum.Common.Infrastructure.CrossTenant.TestPing;

/// <summary>
/// Payload for the <c>Test.Ping.Sent</c> cross-tenant event type.
/// Used to prove the cross-tenant dispatch pipeline works end-to-end.
/// </summary>
public sealed record PingPayload(string Message);
