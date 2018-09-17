// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Superpower.Model;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Ast
{
    /// <summary>
    /// The base class representing a template expression abstract syntax tree (AST) node.
    /// </summary>
    internal abstract class TemplateExpression
    {
        protected TemplateExpression(TextSpan textSpan)
        {
            TextSpan = textSpan;
        }

        public TextSpan TextSpan { get; }

        public abstract TResult Accept<TContext, TResult>(TemplateExpressionVisitor<TContext, TResult> templateExpressionVisitor, TContext context);

        public override string ToString() => TextSpan.ToStringValue();
    }
}
