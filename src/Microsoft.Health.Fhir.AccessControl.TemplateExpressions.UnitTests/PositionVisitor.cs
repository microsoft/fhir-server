// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Ast;
using Superpower.Model;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.UnitTests
{
    internal class PositionVisitor : TemplateExpressionVisitor<Unit, Unit>
    {
        private readonly List<char[]> _markers = new List<char[]>();

        public static string Visit(TemplateExpression expression)
        {
            var visitor = new PositionVisitor();
            expression.Accept(visitor, Unit.Value);
            return string.Join(Environment.NewLine, visitor._markers.Select(arr => new string(arr.Select(c => c == 0 ? ' ' : c).ToArray()).TrimEnd()));
        }

        public override Unit VisitCall(CallTemplateExpression expression, Unit context)
        {
            VisitImpl(expression, '*');
            foreach (var templateExpressionArgument in expression.Arguments)
            {
                templateExpressionArgument.Accept(this, context);
            }

            return context;
        }

        public override Unit VisitInterpolatedString(InterpolatedStringTemplateExpression expression, Unit context)
        {
            VisitImpl(expression, '$');
            foreach (var segment in expression.Segments)
            {
                segment.Accept(this, context);
            }

            return context;
        }

        public override Unit VisitStringLiteral(StringLiteralTemplateExpression expression, Unit context)
        {
            VisitImpl(expression, '\'');
            return context;
        }

        public override Unit VisitNumericLiteral(NumericLiteralTemplateExpression expression, Unit context)
        {
            VisitImpl(expression, '#');
            return context;
        }

        private void VisitImpl(TemplateExpression expression, char representativeChar)
        {
            var array = _markers.FirstOrDefault(arr => arr[expression.TextSpan.Position.Absolute] == 0);
            if (array == null)
            {
                array = new char[_markers.Count > 0 ? _markers[0].Length : expression.TextSpan.Length];
                _markers.Add(array);
            }

            for (int i = 0; i < expression.TextSpan.Length; i++)
            {
                array[i + expression.TextSpan.Position.Absolute] = representativeChar;
            }
        }
    }
}
