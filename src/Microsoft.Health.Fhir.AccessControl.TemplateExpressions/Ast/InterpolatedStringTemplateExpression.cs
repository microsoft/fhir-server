// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Superpower.Model;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Ast
{
    /// <summary>
    /// Represents an interpolated string template expression.
    /// </summary>
    internal class InterpolatedStringTemplateExpression : TemplateExpression
    {
        public InterpolatedStringTemplateExpression(TextSpan textSpan, IReadOnlyList<TemplateExpression> segments)
            : base(textSpan)
        {
            EnsureArg.IsNotNull(segments, nameof(segments));
            Segments = segments;
        }

        public IReadOnlyList<TemplateExpression> Segments { get; }

        public override TResult Accept<TContext, TResult>(TemplateExpressionVisitor<TContext, TResult> templateExpressionVisitor, TContext context)
        {
            return templateExpressionVisitor.VisitInterpolatedString(this, context);
        }
    }
}
