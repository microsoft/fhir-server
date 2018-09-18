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
    /// Represents a function call template expression.
    /// </summary>
    internal class CallTemplateExpression : TemplateExpression
    {
        public CallTemplateExpression(TextSpan textSpan, string identifier, IReadOnlyList<TemplateExpression> arguments)
            : base(textSpan)
        {
            EnsureArg.IsNotNullOrWhiteSpace(identifier, nameof(identifier));
            EnsureArg.IsNotNull(arguments, nameof(arguments));

            Identifier = identifier;
            Arguments = arguments;
        }

        public string Identifier { get; }

        public IReadOnlyList<TemplateExpression> Arguments { get; }

        public override TResult Accept<TContext, TResult>(TemplateExpressionVisitor<TContext, TResult> templateExpressionVisitor, TContext context)
        {
            return templateExpressionVisitor.VisitCall(this, context);
        }
    }
}
