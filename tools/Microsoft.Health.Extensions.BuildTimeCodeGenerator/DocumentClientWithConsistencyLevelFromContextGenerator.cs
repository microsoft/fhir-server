// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Documents.Client;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using IDocumentClient = Microsoft.Azure.Documents.IDocumentClient;

namespace Microsoft.Health.Extensions.BuildTimeCodeGenerator
{
    /// <summary>
    /// Generates a partial class for wrapping IDocumentClient, where for each call, where applicable,
    /// we set ConsistencyLevel and/or SessionToken on <see cref="FeedOptions" /> or <see cref="RequestOptions"/>
    /// parameters based on HTTP request headers, and we set the session consistency output header based on the response.
    /// </summary>
    internal class DocumentClientWithConsistencyLevelFromContextGenerator : ICodeGenerator
    {
        public MemberDeclarationSyntax Generate(string typeName)
        {
            // First generate a basic class that implements the interfaces and delegates to an inner field.
            var generator = new DelegatingInterfaceImplementationGenerator(
                typeModifiers: TokenList(Token(SyntaxKind.InternalKeyword), Token(SyntaxKind.PartialKeyword)),
                constructorModifiers: TokenList(Token(SyntaxKind.PrivateKeyword)),
                typeof(IDocumentClient),
                typeof(IDisposable));

            MemberDeclarationSyntax declaration = generator.Generate(typeName);

            // Assembling a CSharpCompilation for .NET core is non-trivial. The scripting API takes care of
            // this for us, so we just use it instead of constructing one ourselves.

            ScriptOptions scriptOptions = ScriptOptions.Default.WithReferences(typeof(IDocumentClient).Assembly);
            Compilation compilation = CSharpScript.Create(declaration.NormalizeWhitespace().ToString(), scriptOptions).GetCompilation();

            // Ensure that there are no complication errors. Any errors would lead to the ConsistencyLevelRewriter behaving unpredictably.

            var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
            if (errors.Any())
            {
                throw new InvalidOperationException($"Unexpected error diagnostics:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
            }

            // Now rewrite the code with logic specific to this generator

            SyntaxTree syntaxTree = compilation.SyntaxTrees.Single();

            var rewriter = new ConsistencyLevelRewriter(compilation, compilation.GetSemanticModel(syntaxTree));
            var rewrittenCompilationUnit = (CompilationUnitSyntax)rewriter.Visit(syntaxTree.GetRoot());
            return rewrittenCompilationUnit.Members.Single();
        }

        private class ConsistencyLevelRewriter : CSharpSyntaxRewriter
        {
            private readonly SemanticModel _semanticModel;
            private readonly INamedTypeSymbol _taskTypeSymbol;
            private readonly INamedTypeSymbol[] _optionTypeSymbols;
            private readonly INamedTypeSymbol[] _responseTypeSymbols;
            private readonly HashSet<string> _methodsWithOptionOverloads;

            public ConsistencyLevelRewriter(Compilation compilation, SemanticModel semanticModel)
            {
                // look for methods that rely on overloading instead of FeedOptions/RequestOptions being an optional parameter

                var optionParameterTypes = new[] { typeof(FeedOptions), typeof(RequestOptions) };
                _methodsWithOptionOverloads = typeof(IDocumentClient).GetMethods()
                    .GroupBy(m => m.Name)
                    .Where(g =>
                        g.Select(m => m.GetParameters().Any(p => optionParameterTypes.Contains(p.ParameterType))).Distinct().Count() > 1)
                    .Select(g => g.Key).ToHashSet();

                _taskTypeSymbol = compilation.GetTypeByMetadataName(typeof(Task<>).FullName);
                _optionTypeSymbols = optionParameterTypes.Select(t => compilation.GetTypeByMetadataName(t.FullName)).ToArray();
                _responseTypeSymbols = new[] { typeof(DocumentResponse<>), typeof(FeedResponse<>), typeof(ResourceResponse<>), typeof(StoredProcedureResponse<>) }.Select(t => compilation.GetTypeByMetadataName(t.FullName)).ToArray();
                _semanticModel = semanticModel;
            }

            public override SyntaxNode VisitArgument(ArgumentSyntax node)
            {
                ITypeSymbol type = _semanticModel.GetTypeInfo(node.Expression).Type;

                if (_optionTypeSymbols.Contains(type))
                {
                    return node.WithExpression(InvocationExpression(IdentifierName("UpdateOptions")).AddArgumentListArguments(node));
                }

                return base.VisitArgument(node);
            }

            public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
            {
                var visitedNode = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node);

                if (ReferenceEquals(visitedNode, node) &&
                    _methodsWithOptionOverloads.Contains(node.Identifier.ValueText))
                {
                    // Don't generate methods where the body would call an overload.
                    // The logic is a bit more complicated and there are only
                    // two methods anyway. They can be written out by hand in the
                    // partial class.

                    return IncompleteMember();
                }

                if (_semanticModel.GetTypeInfo(node.ReturnType).Type is INamedTypeSymbol type &&
                    type.ConstructedFrom.Equals(_taskTypeSymbol) &&
                    type.TypeArguments[0] is INamedTypeSymbol argumentType &&
                    argumentType.IsGenericType &&
                    _responseTypeSymbols.Contains(argumentType.ConstructedFrom))
                {
                    visitedNode = visitedNode.AddModifiers(Token(SyntaxKind.AsyncKeyword));
                    var invocation = visitedNode.DescendantNodes().OfType<InvocationExpressionSyntax>().First();
                    visitedNode = visitedNode.ReplaceNode(invocation, InvocationExpression(IdentifierName("ProcessResponse")).AddArgumentListArguments(Argument(AwaitExpression(invocation))));

                    return visitedNode;
                }

                return visitedNode;
            }
        }
    }
}
