namespace Liakont.Modules.Reconciliation.Tests.Integration.Fixtures;

using Stratum.Common.Infrastructure.Database;

/// <summary>Une base de tenant de test : sa chaîne de connexion et sa fabrique de connexions.</summary>
public sealed record TenantDatabase(string ConnectionString, IConnectionFactory ConnectionFactory);
