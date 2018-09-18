// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Ast;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Evaluation
{
    /// <summary>
    /// Performs semantic checks on a template expression. Verifies type compatibility, functions being called with the right kind of arguments, etc.
    /// </summary>
    internal class TemplateExpressionTypeChecker : TemplateExpressionVisitor<TemplateExpressionDiagnosticCollection, Type>
    {
        private readonly TemplateExpressionFunctionRepository _functions;

        public TemplateExpressionTypeChecker(TemplateExpressionFunctionRepository functions)
        {
            EnsureArg.IsNotNull(functions, nameof(functions));
            _functions = functions;
        }

        public override Type VisitCall(CallTemplateExpression expression, TemplateExpressionDiagnosticCollection context)
        {
            if (!_functions.Functions.TryGetValue(expression.Identifier, out var functionMetadata))
            {
                context.Add(expression.TextSpan, DiagnosticMessages.UnknownFunction, expression.Identifier, string.Join(", ", _functions.Functions.Keys.OrderBy(n => n).Select(n => $"'{n}'")));
                return typeof(void);
            }

            var argTypes = expression.Arguments.Select(arg => arg.Accept(this, context)).ToList();

            for (int i = 0; i < functionMetadata.ExposedParameters.Length; i++)
            {
                ParameterInfo parameterInfo = functionMetadata.ExposedParameters[i];
                if (argTypes.Count > i)
                {
                    Type argType = argTypes[i];
                    if (argType.IsSubclassOf(typeof(Task)))
                    {
                        argType = argType.GetGenericArguments()[0];
                    }

                    if (!parameterInfo.ParameterType.IsAssignableFrom(argType))
                    {
                        context.Add(expression.Arguments[i].TextSpan, DiagnosticMessages.UnassignableAgument, argType.Name, parameterInfo.Name, parameterInfo.ParameterType.Name);
                    }
                }
                else
                {
                    if (!parameterInfo.IsOptional)
                    {
                        context.Add(expression.TextSpan, DiagnosticMessages.MissingArgument, parameterInfo.Name, functionMetadata.Name);
                    }
                }
            }

            List<TemplateExpression> unexpectedArguments = expression.Arguments.Skip(functionMetadata.ExposedParameters.Length).ToList();
            if (unexpectedArguments.Count > 0)
            {
                context.Add(
                    unexpectedArguments.First().TextSpan.UntilEndOf(unexpectedArguments.Last().TextSpan),
                    DiagnosticMessages.TooManyArguments,
                    expression.Arguments.Count,
                    functionMetadata.Name,
                    functionMetadata.ExposedParameters.Length);
            }

            return functionMetadata.ReturnType;
        }

        public override Type VisitInterpolatedString(InterpolatedStringTemplateExpression expression, TemplateExpressionDiagnosticCollection context)
        {
            foreach (var segment in expression.Segments)
            {
                segment.Accept(this, context);
            }

            return typeof(string);
        }

        public override Type VisitStringLiteral(StringLiteralTemplateExpression expression, TemplateExpressionDiagnosticCollection context)
        {
            return typeof(string);
        }

        public override Type VisitNumericLiteral(NumericLiteralTemplateExpression expression, TemplateExpressionDiagnosticCollection context)
        {
            return typeof(int);
        }
    }
}
