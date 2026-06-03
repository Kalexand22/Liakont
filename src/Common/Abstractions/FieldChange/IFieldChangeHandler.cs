namespace Stratum.Common.Abstractions.FieldChange;

/// <summary>
/// Marker interface for field-change handlers. Individual handler methods are
/// decorated with <see cref="OnChangeAttribute"/> to declare which field they react to.
/// </summary>
/// <typeparam name="T">The entity type whose fields are being observed.</typeparam>
public interface IFieldChangeHandler<T>;
