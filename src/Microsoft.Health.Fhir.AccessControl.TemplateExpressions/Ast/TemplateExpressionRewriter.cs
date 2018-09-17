// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Ast
{
    /// <summary>
    /// An abstract <see cref="TemplateExpressionVisitor{TContext,TemplateExpression}"/> for transforming a template expression tree.
    /// </summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    internal abstract class TemplateExpressionRewriter<TContext> : TemplateExpressionVisitor<TContext, TemplateExpression>
    {
        public override TemplateExpression VisitCall(CallTemplateExpression expression, TContext context)
        {
            var rewrittenArguments = VisitArray(expression.Arguments, context);
            return ReferenceEquals(rewrittenArguments, expression.Arguments) ? expression : new CallTemplateExpression(expression.TextSpan, expression.Identifier, rewrittenArguments);
        }

        public override TemplateExpression VisitInterpolatedString(InterpolatedStringTemplateExpression expression, TContext context)
        {
            var rewrittenSegments = VisitArray(expression.Segments, context);
            return ReferenceEquals(rewrittenSegments, expression.Segments) ? expression : new InterpolatedStringTemplateExpression(expression.TextSpan, rewrittenSegments);
        }

        public override TemplateExpression VisitStringLiteral(StringLiteralTemplateExpression expression, TContext context)
        {
            return expression;
        }

        public override TemplateExpression VisitNumericLiteral(NumericLiteralTemplateExpression expression, TContext context)
        {
            return expression;
        }

        private IReadOnlyList<TemplateExpression> VisitArray(IReadOnlyList<TemplateExpression> inputArray, TContext context)
        {
            TemplateExpression[] outputArray = null;

            for (var index = 0; index < inputArray.Count; index++)
            {
                var argument = inputArray[index];
                var rewrittenArgument = argument.Accept(this, context);
                if (!ReferenceEquals(rewrittenArgument, argument))
                {
                    if (outputArray == null)
                    {
                        outputArray = new TemplateExpression[inputArray.Count];
                        for (int i = 0; i < inputArray.Count; i++)
                        {
                            outputArray[i] = inputArray[i];
                        }
                    }

                    outputArray[index] = rewrittenArgument;
                }
            }

            return outputArray ?? inputArray;
        }
    }
}
