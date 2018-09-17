// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Ast;
using Superpower.Model;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions.Evaluation
{
    /// <summary>
    /// Compiles template expressions using the Roslyn API. This generates much more efficient code for async methods than
    /// <see cref="TemplateExpressionCompiler"/> but with a much higher initial overhead (on the order of a couple of seconds!).
    /// </summary>
    /// <typeparam name="TServiceProvider">The service provider parameter type.</typeparam>
    internal class TemplateExpressionRoslynCompiler<TServiceProvider> : TemplateExpressionVisitor<Unit, (ExpressionSyntax expression, Type type)>
        where TServiceProvider : IServiceProvider
    {
        private const string ServicePrincipalParameterName = "__sp";
        private const string ClassName = "CompiledTemplateExpressions";

        private readonly TemplateExpressionFunctionRepository _functionRepository;

        public TemplateExpressionRoslynCompiler(TemplateExpressionFunctionRepository functionRepository)
        {
            EnsureArg.IsNotNull(functionRepository, nameof(functionRepository));
            _functionRepository = functionRepository;
        }

        private IEnumerable<MemberDeclarationSyntax> GetFields()
        {
            foreach (var function in _functionRepository.Functions.Values)
            {
                yield return FieldDeclaration(
                        VariableDeclaration(GetTypeSyntax(function.Delegate.GetType()))
                            .WithVariables(SingletonSeparatedList(VariableDeclarator(Identifier(function.Name)))))
                    .WithModifiers(TokenList(Token(SyntaxKind.PrivateKeyword), Token(SyntaxKind.ReadOnlyKeyword)));
            }
        }

        private MemberDeclarationSyntax GetConstructor(SyntaxToken className)
        {
            return ConstructorDeclaration(className)
                .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                .WithParameterList(ParameterList(SeparatedList(
                    _functionRepository.Functions.Values.Select(f => Parameter(Identifier(f.Name)).WithType(GetTypeSyntax(f.Delegate.GetType()))))))
                .WithBody(Block(
                    _functionRepository.Functions.Values.Select(f => ExpressionStatement(
                        AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                ThisExpression(),
                                IdentifierName(f.Name)),
                            IdentifierName(f.Name))))));
        }

        private MemberDeclarationSyntax GetEvaluateMethod(string methodName, TemplateExpression templateExpression)
        {
            var (expression, type) = templateExpression.Accept(this, Unit.Value);

            bool isAsync = expression.DescendantNodesAndSelf().Any(node => node.IsKind(SyntaxKind.AwaitExpression));

            if (type.IsSubclassOf(typeof(Task)) && (isAsync || type != typeof(Task<string>)))
            {
                isAsync = true;
                expression = AwaitExpression(expression);
            }

            if (!isAsync)
            {
                expression = ObjectCreationExpression(GetTypeSyntax(typeof(ValueTask<string>))).AddArgumentListArguments(Argument(expression));
            }

            MethodDeclarationSyntax method = MethodDeclaration(GetTypeSyntax(typeof(ValueTask<string>)), methodName).AddModifiers(Token(SyntaxKind.PublicKeyword));

            if (isAsync)
            {
                method = method.AddModifiers(Token(SyntaxKind.AsyncKeyword));
            }

            return method
                .WithParameterList(ParameterList(SingletonSeparatedList(Parameter(Identifier(ServicePrincipalParameterName)).WithType(GetTypeSyntax(typeof(TServiceProvider))))))
                .WithBody(Block(SingletonList(ReturnStatement(expression))));
        }

        public IReadOnlyDictionary<string, Func<TServiceProvider, ValueTask<string>>> CompileBatch(IReadOnlyDictionary<string, TemplateExpression> expressions)
        {
            string MethodName(string expressionName) => "Evaluate_" + expressionName;

            var compilationUnit = CompilationUnit()
                .AddMembers(
                    ClassDeclaration(ClassName)
                        .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                        .WithMembers(
                            List(GetFields())
                                .Add(GetConstructor(Identifier(ClassName)))
                                .AddRange(expressions.Select(p => GetEvaluateMethod(MethodName(p.Key), p.Value)))));

            var compilation = CSharpCompilation.Create("CompiledTemplateExpressionAssembly")
                .WithOptions(
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release))
                .AddReferences(_functionRepository.Functions.Values
                    .Select(f => f.Delegate.GetType())
                    .Concat(new[] { typeof(object), typeof(TServiceProvider) })
                    .SelectMany(t => new[] { t.Assembly }.Concat(t.Assembly.GetReferencedAssemblies().Select(Assembly.Load)))
                    .Distinct()
                    .Select(a => MetadataReference.CreateFromFile(a.Location)))
                .AddSyntaxTrees(compilationUnit.SyntaxTree);

            var stream = new MemoryStream();
            EmitResult compilationResult = compilation.Emit(stream);
            if (!compilationResult.Success)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, compilationResult.Diagnostics.Select(codeIssue => codeIssue.GetMessage())));
            }

            stream.Seek(0, 0);
            Assembly asm = Assembly.Load(stream.ToArray());
            Type type = asm.GetType(ClassName);
            object instance = Activator.CreateInstance(type, _functionRepository.Functions.Select(f => (object)f.Value.Delegate).ToArray());

            return expressions.ToDictionary(
                p => p.Key,
                p => (Func<TServiceProvider, ValueTask<string>>)Delegate.CreateDelegate(typeof(Func<TServiceProvider, ValueTask<string>>), instance, type.GetMethod(MethodName(p.Key))),
                StringComparer.Ordinal);
        }

        public Func<TServiceProvider, ValueTask<string>> Compile(TemplateExpression templateExpression)
        {
            return CompileBatch(new Dictionary<string, TemplateExpression> { { string.Empty, templateExpression } }).Single().Value;
        }

        private static NameSyntax GetTypeSyntax(Type t)
        {
            NameSyntax qualification = t.IsNested
                ? GetTypeSyntax(t.DeclaringType)
                : t.Namespace.Split('.').Select(s => (NameSyntax)IdentifierName(s)).Aggregate((acc, next) => QualifiedName(acc, (SimpleNameSyntax)next));

            SimpleNameSyntax name = t.IsGenericType
                ? GenericName(t.Name.Substring(0, t.Name.IndexOf('`', StringComparison.Ordinal)))
                    .WithTypeArgumentList(TypeArgumentList(SeparatedList(t.GetGenericArguments().Select(type => (TypeSyntax)GetTypeSyntax(type)))))
                : (SimpleNameSyntax)IdentifierName(t.Name);

            return QualifiedName(qualification, name);
        }

        private static ExpressionSyntax AwaitIfNecessary((ExpressionSyntax expression, Type type) expression)
        {
            return expression.type.IsSubclassOf(typeof(Task)) ? AwaitExpression(expression.expression) : expression.expression;
        }

        public override (ExpressionSyntax expression, Type type) VisitCall(CallTemplateExpression expression, Unit context)
        {
            FunctionMetadata function = _functionRepository.Functions[expression.Identifier];
            Type returnType = function.ReturnType;

            var identifier = function.Delegate.Target == null && function.Delegate.Method.DeclaringType.IsPublic && function.Delegate.Method.IsPublic
                ? (NameSyntax)QualifiedName(GetTypeSyntax(function.Delegate.Method.DeclaringType), IdentifierName(function.Delegate.Method.Name))
                : IdentifierName(expression.Identifier);

            IEnumerable<ExpressionSyntax> GetAllArguments()
            {
                using (IEnumerator<ExpressionSyntax> specifiedArguments = expression.Arguments.Select(a => a.Accept(this, context)).Select(AwaitIfNecessary).GetEnumerator())
                {
                    foreach (var parameterInfo in function.AllParameters)
                    {
                        if (parameterInfo.IsDefined(typeof(InjectedAttribute), false))
                        {
                            var typedProperties = typeof(TServiceProvider).GetProperties().Where(p => p.PropertyType == parameterInfo.ParameterType).ToList();

                            if (typedProperties.Count == 1)
                            {
                                yield return MemberAccessExpression(
                                    SyntaxKind.SimpleMemberAccessExpression,
                                    IdentifierName(ServicePrincipalParameterName),
                                    IdentifierName(typedProperties[0].Name));
                            }
                            else
                            {
                                yield return CastExpression(
                                    GetTypeSyntax(parameterInfo.ParameterType),
                                    InvocationExpression(
                                        MemberAccessExpression(
                                            SyntaxKind.SimpleMemberAccessExpression,
                                            IdentifierName(ServicePrincipalParameterName),
                                            IdentifierName(nameof(IServiceProvider.GetService))),
                                        ArgumentList(SingletonSeparatedList(Argument(TypeOfExpression(GetTypeSyntax(parameterInfo.ParameterType)))))));
                            }
                        }
                        else if (specifiedArguments.MoveNext())
                        {
                            yield return specifiedArguments.Current;
                        }
                    }
                }
            }

            return (
                InvocationExpression(identifier, ArgumentList(SeparatedList(GetAllArguments().Select(Argument)))),
                returnType);
        }

        public override (ExpressionSyntax expression, Type type) VisitInterpolatedString(InterpolatedStringTemplateExpression expression, Unit context)
        {
            var visitedArguments = expression.Segments.Select(a => a.Accept(this, context)).ToList();

            if (visitedArguments.Count == 1 && (visitedArguments[0].type == typeof(string) || visitedArguments[0].type == typeof(Task<string>)))
            {
                return visitedArguments[0];
            }

            return (
                InvocationExpression(MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        PredefinedType(Token(SyntaxKind.StringKeyword)),
                        IdentifierName(nameof(string.Concat))))
                    .AddArgumentListArguments(visitedArguments.Select(AwaitIfNecessary).Select(Argument).ToArray()),
                typeof(string));
        }

        public override (ExpressionSyntax expression, Type type) VisitStringLiteral(StringLiteralTemplateExpression expression, Unit context)
        {
            return
                (LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(expression.Value)),
                typeof(string));
        }

        public override (ExpressionSyntax expression, Type type) VisitNumericLiteral(NumericLiteralTemplateExpression expression, Unit context)
        {
            return (
                LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(expression.Value)),
                typeof(int));
        }
    }
}
