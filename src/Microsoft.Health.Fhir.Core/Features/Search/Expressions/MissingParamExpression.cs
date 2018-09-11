// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents an expression that indicates the search parameter should be missing.
    /// </summary>
    public class MissingParamExpression : Expression
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MissingParamExpression"/> class.
        /// </summary>
        /// <param name="paramName">The search parameter name.</param>
        /// <param name="isMissing">A flag indicating whether the parameter should be missing or not.</param>
        public MissingParamExpression(string paramName, bool isMissing)
        {
            EnsureArg.IsNotNullOrWhiteSpace(paramName, nameof(paramName));

            ParamName = paramName;
            IsMissing = isMissing;
        }

        /// <summary>
        /// Gets the search parameter name.
        /// </summary>
        public string ParamName { get; }

        /// <summary>
        /// Gets a value indicating whether the parameter should be missing or not.
        /// </summary>
        public bool IsMissing { get; }

        protected internal override void AcceptVisitor(IExpressionVisitor visitor)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            visitor.Visit(this);
        }
    }
}
