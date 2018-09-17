// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Ast;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.UnitTests
{
    internal class TestStringTemplateExpressionVisitor : TemplateExpressionVisitor<StringBuilder, StringBuilder>
    {
        public static readonly TestStringTemplateExpressionVisitor Instance = new TestStringTemplateExpressionVisitor();

        public override StringBuilder VisitCall(CallTemplateExpression expression, StringBuilder context)
        {
            context.Append("(call ").Append(expression.Identifier);
            foreach (var argument in expression.Arguments)
            {
                context.Append(' ');
                argument.Accept(this, context);
            }

            return context.Append(')');
        }

        public override StringBuilder VisitInterpolatedString(InterpolatedStringTemplateExpression expression, StringBuilder context)
        {
            context.Append("(concat");
            foreach (var innerExpression in expression.Segments)
            {
                context.Append(' ');
                innerExpression.Accept(this, context);
            }

            return context.Append(')');
        }

        public override StringBuilder VisitStringLiteral(StringLiteralTemplateExpression expression, StringBuilder context)
        {
            return context.Append('\'').Append(expression.Value.Replace("'", "\\'")).Append('\'');
        }

        public override StringBuilder VisitNumericLiteral(NumericLiteralTemplateExpression expression, StringBuilder context)
        {
            return context.Append(expression.Value);
        }
    }
}
