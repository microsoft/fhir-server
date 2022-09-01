// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions
{
    /// <summary>
    /// The different kinds of <see cref="SearchParamTableExpression"/>s.
    /// </summary>
    internal enum SearchParamTableExpressionKind
    {
        /// <summary>
        /// Represents a table expression that applies a filter, producing a set of candidate resource IDs.
        /// This set is intersected with its preceding table expression, if any.
        /// </summary>
        Normal,

        /// <summary>
        /// Represents a table expression that applies a filter, producing a set of candidate resource IDs.
        /// This set is appended to the set produced by its preceding table expression.
        /// </summary>
        Concatenation,

        /// <summary>
        /// Represents a table expression that excludes items, produced by applying a filter, from its
        /// preceding table expression.
        /// </summary>
        NotExists,

        /// <summary>
        /// Represents a table expression that yields all possible resource IDs.
        /// </summary>
        All,

        /// <summary>
        /// Represents a table expression that applies a TOP operator over its predecessor.
        /// </summary>
        Top,

        /// <summary>
        /// Represents a table expression that serves as the JOIN between a resource and target reference.
        /// in a chained search.
        /// </summary>
        Chain,

        /// <summary>
        /// Represents a table expression that is used to include multiple resource types in the query.
        /// </summary>
        Include,

        /// <summary>
        /// Represents a table expression that is used to UNION results from multiple queries together.
        /// </summary>
        Union,

        /// <summary>
        /// Represents a table expression that is used to union all of the includes with the base search query.
        /// </summary>
        IncludeUnionAll,

        /// <summary>
        /// Represents a table expression that is used to sort result of the base search query.
        /// </summary>
        Sort,

        /// <summary>
        /// Represents a table expression that is used to limit the number of included items.
        /// </summary>
        IncludeLimit,

        /// <summary>
        /// Represents a table expression that is used to sort the result of the base query where
        /// the sort parameter is also present as a query parameter in the base search query.
        /// </summary>
        SortWithFilter,
    }
}
