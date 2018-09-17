// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Superpower.Model;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Ast
{
    /// <summary>
    /// Represents a numeric literal template expression.
    /// </summary>
    internal class NumericLiteralTemplateExpression : TemplateExpression, IConstantTemplateExpression
    {
        public NumericLiteralTemplateExpression(TextSpan textSpan, int value)
            : base(textSpan)
        {
            Value = value;
        }

        public int Value { get; }

        object IConstantTemplateExpression.Value => Value;

        public override TResult Accept<TContext, TResult>(TemplateExpressionVisitor<TContext, TResult> templateExpressionVisitor, TContext context)
        {
            return templateExpressionVisitor.VisitNumericLiteral(this, context);
        }
    }
}
