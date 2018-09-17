// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Superpower.Model;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Ast
{
    /// <summary>
    /// Represents a string literal template expression.
    /// </summary>
    internal class StringLiteralTemplateExpression : TemplateExpression, IConstantTemplateExpression
    {
        public StringLiteralTemplateExpression(TextSpan textSpan, string value)
            : base(textSpan)
        {
            EnsureArg.IsNotNull(value, nameof(value));
            Value = value;
        }

        public string Value { get; }

        object IConstantTemplateExpression.Value => Value;

        public override TResult Accept<TContext, TResult>(TemplateExpressionVisitor<TContext, TResult> templateExpressionVisitor, TContext context)
        {
            return templateExpressionVisitor.VisitStringLiteral(this, context);
        }
    }
}
