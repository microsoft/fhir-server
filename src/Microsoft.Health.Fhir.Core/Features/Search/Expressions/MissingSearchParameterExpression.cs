// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents an expression that indicates the search parameter should be missing.
    /// </summary>
    public class MissingSearchParameterExpression : SearchParameterExpressionBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MissingSearchParameterExpression"/> class.
        /// </summary>
        /// <param name="searchParameter">The search parameter this expression is bound to.</param>
        /// <param name="isMissing">A flag indicating whether the parameter should be missing or not.</param>
        public MissingSearchParameterExpression(SearchParameterInfo searchParameter, bool isMissing)
            : base(searchParameter)
        {
            IsMissing = isMissing;
        }

        /// <summary>
        /// Gets a value indicating whether the parameter should be missing or not.
        /// </summary>
        public bool IsMissing { get; }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            return visitor.VisitMissingSearchParameter(this, context);
        }

        public override string ToString()
        {
            return $"({(!IsMissing ? "Not" : null)}MissingParam {Parameter.Name})";
        }
    }
}
