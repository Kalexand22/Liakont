namespace Stratum.Common.Abstractions.Grid;

using System.Linq.Expressions;

/// <summary>
/// Builds LINQ <see cref="Expression{TDelegate}"/> predicates from a
/// <see cref="FilterGroup"/>. Resolves property paths including
/// related-table dot-notation (e.g. "Customer.City").
/// </summary>
/// <typeparam name="TItem">The entity type being filtered.</typeparam>
public interface IFilterExpressionBuilder<TItem>
{
    /// <summary>
    /// Builds a predicate expression from the given filter group.
    /// </summary>
    /// <param name="group">Root filter group.</param>
    /// <returns>
    /// A compiled expression that can be used with LINQ-to-Objects or
    /// passed to an ORM query provider.
    /// </returns>
    Expression<Func<TItem, bool>> Build(FilterGroup group);
}
