// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Ast;
using Superpower.Model;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Evaluation
{
    /// <summary>
    /// Simplifies template expression trees by folding constant expressions. For example a{'b'}c is rewritten as abc.
    /// </summary>
    internal class ConstantFolder : TemplateExpressionRewriter<Unit>
    {
        public static readonly ConstantFolder Instance = new ConstantFolder();

        public override TemplateExpression VisitInterpolatedString(InterpolatedStringTemplateExpression expression, Unit context)
        {
            var visitedExpression = base.VisitInterpolatedString(expression, context);
            if (visitedExpression is InterpolatedStringTemplateExpression visitedStringTemplateExpression &&
                visitedStringTemplateExpression.Segments.All(expr => expr is IConstantTemplateExpression))
            {
                return new StringLiteralTemplateExpression(
                    visitedExpression.TextSpan,
                    string.Concat(visitedStringTemplateExpression.Segments.Select(expr => ((IConstantTemplateExpression)expr).Value)));
            }

            return visitedExpression;
        }
    }
}
