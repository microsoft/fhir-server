// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Ast
{
    /// <summary>
    /// The base visitor contract for <see cref="TemplateExpression"/>s.
    /// </summary>
    /// <typeparam name="TContext">A context object that is passed in to all Visit methods.</typeparam>
    /// <typeparam name="TResult">The result type of the visitation.</typeparam>
    internal abstract class TemplateExpressionVisitor<TContext, TResult>
    {
        public abstract TResult VisitCall(CallTemplateExpression expression, TContext context);

        public abstract TResult VisitInterpolatedString(InterpolatedStringTemplateExpression expression, TContext context);

        public abstract TResult VisitStringLiteral(StringLiteralTemplateExpression expression, TContext context);

        public abstract TResult VisitNumericLiteral(NumericLiteralTemplateExpression expression, TContext context);
    }
}
