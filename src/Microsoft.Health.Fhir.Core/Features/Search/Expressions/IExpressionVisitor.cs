// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents a visitor for expression tree.
    /// </summary>
    public interface IExpressionVisitor
    {
        /// <summary>
        /// Visits the <see cref="BinaryExpression"/>.
        /// </summary>
        /// <param name="expression">The expression to visit.</param>
        void Visit(BinaryExpression expression);

        /// <summary>
        /// Visits the <see cref="ChainedExpression"/>.
        /// </summary>
        /// <param name="expression">The expression to visit.</param>
        void Visit(ChainedExpression expression);

        /// <summary>
        /// Visits the <see cref="MissingFieldExpression"/>.
        /// </summary>
        /// <param name="expression">The expression to visit.</param>
        void Visit(MissingFieldExpression expression);

        /// <summary>
        /// Visits the <see cref="MissingParamExpression"/>.
        /// </summary>
        /// <param name="expression">The expression to visit.</param>
        void Visit(MissingParamExpression expression);

        /// <summary>
        /// Visits the <see cref="MultiaryExpression"/>.
        /// </summary>
        /// <param name="expression">The expression to visit.</param>
        void Visit(MultiaryExpression expression);

        /// <summary>
        /// Visits the <see cref="StringExpression"/>.
        /// </summary>
        /// <param name="expression">The expression to visit.</param>
        void Visit(StringExpression expression);
    }
}
