// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions
{
    /// <summary>
    /// The different kinds of <see cref="TableExpression"/>s.
    /// </summary>
    internal enum TableExpressionKind
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
        /// Represents a table expression that yields all possible resource IDs
        /// </summary>
        All,

        /// <summary>
        /// Represents a table expression that applies a TOP operator over its predecessor.
        /// </summary>
        Top,

        /// <summary>
        /// Represents a table expression that serves as the JOIN between a resource and target reference
        /// in a chained search.
        /// </summary>
        Chain,
    }
}
