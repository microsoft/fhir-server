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
    /// A compiler for template expressions that emits and compiles a LINQ expression tree. Since async/await is not supported in LINQ expressions,
    /// we use a few of helper methods (<see cref="AwaitAndApply{TInput,TOutput}"/>, <see cref="AwaitAndApplyAsync{TInput,TOutput}"/>, and <see cref="Combine{TFirst,TSecond}"/>) as building blocks for an alternative.
    /// This is not nearly as efficient though as the C# compiler's codegen.
    /// </summary>
    /// <typeparam name="TServiceProvider">The service provider parameter type.</typeparam>
    internal class TemplateExpressionCompiler<TServiceProvider> : TemplateExpressionVisitor<ParameterExpression, Expression>
        where TServiceProvider : IServiceProvider
    {
        private readonly TemplateExpressionFunctionRepository _functionRepository;

        private static readonly MethodInfo Concat2MethodInfo = typeof(string).GetMethod(nameof(string.Concat), new[] { typeof(object), typeof(object) });
        private static readonly MethodInfo Concat3MethodInfo = typeof(string).GetMethod(nameof(string.Concat), new[] { typeof(object), typeof(object), typeof(object) });
        private static readonly MethodInfo ConcatNMethodInfo = typeof(string).GetMethod(nameof(string.Concat), new[] { typeof(object[]) });
        private static readonly MethodInfo ToStringMethodInfo = typeof(object).GetMethod(nameof(ToString));
        private static readonly MethodInfo CombineMethodInfo = typeof(TemplateExpressionCompiler<TServiceProvider>).GetMethod(nameof(Combine), BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo AwaitAndApplyMethodInfo = typeof(TemplateExpressionCompiler<TServiceProvider>).GetMethod(nameof(AwaitAndApply), BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo AwaitAndApplyAsyncMethodInfo = typeof(TemplateExpressionCompiler<TServiceProvider>).GetMethod(nameof(AwaitAndApplyAsync), BindingFlags.Static | BindingFlags.NonPublic);

        public TemplateExpressionCompiler(TemplateExpressionFunctionRepository functionRepository)
        {
            EnsureArg.IsNotNull(functionRepository, nameof(functionRepository));
            _functionRepository = functionRepository;
        }

        public Func<TServiceProvider, ValueTask<string>> Compile(TemplateExpression templateExpression)
        {
            var serviceProviderParameter = Expression.Parameter(typeof(TServiceProvider), "serviceProvider");
            Expression body = templateExpression.Accept(this, serviceProviderParameter);

            body = Expression.New(typeof(ValueTask<string>).GetConstructor(new[] { body.Type }), body);

            body = CompileAllLambdasVisitor.Instance.Visit(body);

            return Expression.Lambda<Func<TServiceProvider, ValueTask<string>>>(body, serviceProviderParameter).Compile();
        }

        public override Expression VisitCall(CallTemplateExpression expression, ParameterExpression context)
        {
            FunctionMetadata functionMetadata = _functionRepository.Functions[expression.Identifier];

            IEnumerable<Expression> GetAllArguments(IEnumerable<Expression> specifiedArguments)
            {
                using (IEnumerator<Expression> enumerator = specifiedArguments.GetEnumerator())
                {
                    bool isArgumentSpecified = enumerator.MoveNext();
                    foreach (var parameterInfo in functionMetadata.AllParameters)
                    {
                        if (parameterInfo.GetCustomAttribute<InjectedAttribute>() == null)
                        {
                            if (isArgumentSpecified)
                            {
                                yield return ConvertIfNecessary(enumerator.Current, parameterInfo.ParameterType);
                                isArgumentSpecified = enumerator.MoveNext();
                            }
                            else
                            {
                                yield return Expression.Constant(parameterInfo.DefaultValue, parameterInfo.ParameterType);
                            }
                        }
                        else
                        {
                            var typedProperties = typeof(TServiceProvider).GetProperties().Where(p => p.PropertyType == parameterInfo.ParameterType).ToList();
                            yield return typedProperties.Count == 1
                                ? (Expression)Expression.Property(context, typedProperties[0])
                                : Expression.Convert(
                                    Expression.Call(context, context.Type.GetMethod(nameof(IServiceProvider.GetService)), Expression.Constant(parameterInfo.ParameterType, typeof(Type))),
                                    parameterInfo.ParameterType);
                        }
                    }
                }
            }

            return GenerateAwaitAndApply(
                expression.Arguments.Select(e => e.Accept(this, context)).ToList(),
                args => Expression.Call(
                    functionMetadata.Target == null ? null : Expression.Constant(functionMetadata.Target),
                    functionMetadata.MethodInfo,
                    GetAllArguments(args)),
                context);
        }

        public override Expression VisitInterpolatedString(InterpolatedStringTemplateExpression expression, ParameterExpression context)
        {
            var arguments = expression.Segments.Select(s => s.Accept(this, context)).ToList();

            Expression CallToString(IEnumerable<Expression> args)
            {
                return Expression.Call(args.Single(), ToStringMethodInfo);
            }

            Expression CallConcat2Or3(IEnumerable<Expression> args)
            {
                return Expression.Call(null, arguments.Count == 2 ? Concat2MethodInfo : Concat3MethodInfo, args.Select(a => ConvertIfNecessary(a, typeof(object))));
            }

            Expression CallConcatN(IEnumerable<Expression> args)
            {
                return Expression.Call(null, ConcatNMethodInfo, Expression.NewArrayInit(typeof(object), args.Select(a => ConvertIfNecessary(a, typeof(object)))));
            }

            switch (arguments.Count)
            {
                case 1 when arguments[0].Type == typeof(string) || arguments[0].Type == typeof(Task<string>):
                    return arguments[0];
                case 1:
                    return GenerateAwaitAndApply(arguments, CallToString, context);
                case 2:
                case 3:
                    return GenerateAwaitAndApply(arguments, CallConcat2Or3, context);
                default:
                    return GenerateAwaitAndApply(arguments, CallConcatN, context);
            }
        }

        public override Expression VisitStringLiteral(StringLiteralTemplateExpression expression, ParameterExpression context)
        {
            return Expression.Constant(expression.Value);
        }

        public override Expression VisitNumericLiteral(NumericLiteralTemplateExpression expression, ParameterExpression context)
        {
            return Expression.Constant(expression.Value);
        }

        private static Expression GenerateAwaitAndApply(IList<Expression> arguments, Func<IEnumerable<Expression>, Expression> apply, ParameterExpression serviceProviderParameter)
        {
            if (arguments.All(a => !a.Type.IsSubclassOf(typeof(Task))))
            {
                return apply(arguments);
            }

            bool CanBeEvaluatedOutOfOrder(Expression argument) => argument.NodeType == ExpressionType.Constant;

            var argumentPaths = arguments.Select(arg =>
                    (combined: CanBeEvaluatedOutOfOrder(arg) ? arg : WrapInValueTask(arg),
                        path: new Stack<string>()))
                .ToArray();

            var combinedArguments = argumentPaths.Select(p => p.combined).Where(p => p.NodeType != ExpressionType.Constant).ToList();
            while (combinedArguments.Count > 1)
            {
                for (int i = 0; i < combinedArguments.Count / 2; i++)
                {
                    var arg1 = combinedArguments[2 * i];
                    var arg2 = combinedArguments[(2 * i) + 1];

                    var combined = Expression.Call(
                        null,
                        CombineMethodInfo.MakeGenericMethod(arg1.Type.GetGenericArguments()[0], arg2.Type.GetGenericArguments()[0]),
                        arg1,
                        Expression.Lambda(arg2, Expression.Parameter(typeof(TServiceProvider), "serviceProvider")),
                        serviceProviderParameter);

                    combinedArguments[i] = combined;

                    for (var index = 0; index < argumentPaths.Length; index++)
                    {
                        var argumentPath = argumentPaths[index];

                        if (argumentPath.combined == arg1 || argumentPath.combined == arg2)
                        {
                            argumentPath.path.Push(argumentPath.combined == arg1 ? "Item1" : "Item2");
                            argumentPath.combined = combined;
                            argumentPaths[index] = argumentPath;
                        }
                    }
                }

                if (combinedArguments.Count % 2 != 0)
                {
                    combinedArguments[combinedArguments.Count / 2] = combinedArguments[combinedArguments.Count - 1];
                }

                combinedArguments.RemoveRange((combinedArguments.Count + 1) / 2, combinedArguments.Count / 2);
            }

            var finalCombined = new ReplaceServiceProviderVisitor(serviceProviderParameter).Visit(combinedArguments[0]);

            Expression applyLambdaParameter = Expression.Parameter(finalCombined.Type.GetGenericArguments()[0]);
            Expression applyLambdaBody = apply(
                argumentPaths.Select(p =>
                    CanBeEvaluatedOutOfOrder(p.combined)
                        ? p.combined
                        : p.path.Aggregate(applyLambdaParameter, Expression.PropertyOrField)));

            MethodInfo awaitAndApplyMethod =
                applyLambdaBody.Type.IsSubclassOf(typeof(Task))
                    ? AwaitAndApplyAsyncMethodInfo.MakeGenericMethod(applyLambdaParameter.Type, applyLambdaBody.Type.GetGenericArguments()[0])
                    : AwaitAndApplyMethodInfo.MakeGenericMethod(applyLambdaParameter.Type, applyLambdaBody.Type);

            return Expression.Call(
                null,
                awaitAndApplyMethod,
                finalCombined,
                Expression.Lambda(applyLambdaBody, (ParameterExpression)applyLambdaParameter));
        }

        private static Expression WrapInValueTask(Expression input)
        {
            Type innerType = input.Type.IsSubclassOf(typeof(Task)) ? input.Type.GetGenericArguments()[0] : input.Type;
            return Expression.New(typeof(ValueTask<>).MakeGenericType(innerType).GetConstructor(new[] { input.Type }), input);
        }

        private static Expression ConvertIfNecessary(Expression expression, Type targetType)
        {
            if (expression.Type == targetType ||
                (!targetType.IsValueType && !expression.Type.IsValueType && targetType.IsAssignableFrom(expression.Type)))
            {
                return expression;
            }

            return Expression.Convert(expression, targetType);
        }

        private static async ValueTask<(TFirst, TSecond)> Combine<TFirst, TSecond>(ValueTask<TFirst> firstTask, Func<TServiceProvider, ValueTask<TSecond>> produceNextTask, TServiceProvider serviceProvider)
        {
            return (await firstTask, await produceNextTask(serviceProvider));
        }

        private static async Task<TOutput> AwaitAndApply<TInput, TOutput>(ValueTask<TInput> input, Func<TInput, TOutput> function)
        {
            return function(await input);
        }

        private static async Task<TOutput> AwaitAndApplyAsync<TInput, TOutput>(ValueTask<TInput> input, Func<TInput, Task<TOutput>> function)
        {
            return await function(await input);
        }

        private class ReplaceServiceProviderVisitor : ExpressionVisitor
        {
            private readonly ParameterExpression _outerParameter;
            private ParameterExpression _activeParameter;

            public ReplaceServiceProviderVisitor(ParameterExpression outerParameter)
            {
                _outerParameter = outerParameter;
            }

            protected override Expression VisitLambda<T>(Expression<T> node)
            {
                ParameterExpression snapshot = _activeParameter;
                _activeParameter = node.Parameters[0];
                Expression visited = base.VisitLambda(node);
                _activeParameter = snapshot;
                return visited;
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                if (node == _outerParameter && _activeParameter != null)
                {
                    return _activeParameter;
                }

                return base.VisitParameter(node);
            }
        }

        private class CompileAllLambdasVisitor : ExpressionVisitor
        {
            public static readonly CompileAllLambdasVisitor Instance = new CompileAllLambdasVisitor();

            protected override Expression VisitLambda<T>(Expression<T> node)
            {
                return Expression.Constant(((Expression<T>)base.VisitLambda(node)).Compile());
            }
        }
    }
}
