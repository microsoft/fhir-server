// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Ast;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Evaluation
{
    /// <summary>
    /// A simple template expression interpreter.
    /// </summary>
    /// <typeparam name="TServiceProvider">The service provider parameter type.</typeparam>
    internal class TemplateExpressionInterpreter<TServiceProvider> : TemplateExpressionVisitor<TServiceProvider, ValueTask<object>>
        where TServiceProvider : IServiceProvider
    {
        private readonly Dictionary<string, Func<TServiceProvider, object[], object>> _functionInvokers;

        public TemplateExpressionInterpreter(TemplateExpressionFunctionRepository functionRepository)
        {
            EnsureArg.IsNotNull(functionRepository, nameof(functionRepository));
            _functionInvokers = functionRepository.Functions.ToDictionary(f => f.Key, f => BuildFunctionCaller(f.Value), StringComparer.Ordinal);
        }

        public async ValueTask<string> Evaluate(TemplateExpression expression, TServiceProvider context)
        {
            return (await expression.Accept(this, context)).ToString();
        }

        public override async ValueTask<object> VisitCall(CallTemplateExpression expression, TServiceProvider context)
        {
            object[] arguments = new object[expression.Arguments.Count];
            for (var i = 0; i < expression.Arguments.Count; i++)
            {
                arguments[i] = await expression.Arguments[i].Accept(this, context);
            }

            object returnValue = _functionInvokers[expression.Identifier].Invoke(context, arguments);
            return returnValue is Task<object> task ? await task : returnValue;
        }

        public override async ValueTask<object> VisitInterpolatedString(InterpolatedStringTemplateExpression expression, TServiceProvider context)
        {
            object[] arguments = new object[expression.Segments.Count];
            for (var i = 0; i < expression.Segments.Count; i++)
            {
                arguments[i] = await expression.Segments[i].Accept(this, context);
            }

            return string.Concat(arguments);
        }

        public override ValueTask<object> VisitStringLiteral(StringLiteralTemplateExpression expression, TServiceProvider context)
        {
            return new ValueTask<object>(expression.Value);
        }

        public override ValueTask<object> VisitNumericLiteral(NumericLiteralTemplateExpression expression, TServiceProvider context)
        {
            return new ValueTask<object>(expression.Value);
        }

        private Func<TServiceProvider, object[], object> BuildFunctionCaller(FunctionMetadata function)
        {
            ParameterExpression serviceProviderParameter = Expression.Parameter(typeof(TServiceProvider), "serviceProvider");
            ParameterExpression argsParameters = Expression.Parameter(typeof(object[]), "args");

            Expression[] arguments = new Expression[function.AllParameters.Length];

            for (int i = 0, specifiedArgumentIndex = 0; i < function.AllParameters.Length; i++)
            {
                var parameterInfo = function.AllParameters[i];

                if (parameterInfo.GetCustomAttribute<InjectedAttribute>() == null)
                {
                    arguments[i] = Expression.Convert(
                        Expression.ArrayAccess(argsParameters, Expression.Constant(specifiedArgumentIndex)),
                        parameterInfo.ParameterType);

                    if (parameterInfo.IsOptional)
                    {
                        arguments[i] = Expression.Condition(
                            test: Expression.LessThan(Expression.ArrayLength(argsParameters), Expression.Constant(specifiedArgumentIndex + 1)),
                            ifTrue: Expression.Constant(parameterInfo.DefaultValue, parameterInfo.ParameterType),
                            ifFalse: arguments[i]);
                    }

                    specifiedArgumentIndex++;
                }
                else
                {
                    var typedProperties = typeof(TServiceProvider).GetProperties().Where(p => p.PropertyType == parameterInfo.ParameterType).ToList();

                    arguments[i] = typedProperties.Count == 1
                            ? (Expression)Expression.Property(serviceProviderParameter, typedProperties[0])
                            : Expression.Convert(
                                Expression.Call(serviceProviderParameter, serviceProviderParameter.Type.GetMethod(nameof(IServiceProvider.GetService)), Expression.Constant(parameterInfo.ParameterType, typeof(Type))),
                                parameterInfo.ParameterType);
                }
            }

            Expression call = Expression.Call(
                function.Target == null ? null : Expression.Constant(function.Target),
                function.MethodInfo,
                arguments);

            if (function.ReturnType.IsSubclassOf(typeof(Task)) && !typeof(Task<object>).IsAssignableFrom(function.ReturnType))
            {
                call = Expression.Call(
                    null,
                    GetType().GetMethod(nameof(CastTask), BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(function.ReturnType.GetGenericArguments()[0]),
                    call);
            }
            else if (function.ReturnType.IsValueType)
            {
                call = Expression.Convert(call, typeof(object));
            }

            return Expression.Lambda<Func<TServiceProvider, object[], object>>(
                call,
                serviceProviderParameter,
                argsParameters).Compile();
        }

        private static async Task<object> CastTask<T>(Task<T> task) => await task;
    }
}
