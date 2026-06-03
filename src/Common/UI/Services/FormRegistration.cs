namespace Stratum.Common.UI.Services;

public sealed record FormRegistration(Type EntityType, Type FormType, string? ContextKey);
