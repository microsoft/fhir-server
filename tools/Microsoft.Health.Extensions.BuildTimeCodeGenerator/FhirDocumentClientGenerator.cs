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
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using IDocumentClient = Microsoft.Azure.Documents.IDocumentClient;

namespace Microsoft.Health.Extensions.BuildTimeCodeGenerator
{
    /// <summary>
    /// Generates a partial class for wrapping IDocumentClient, where for each call, where applicable,
    /// we set ConsistencyLevel and/or SessionToken on <see cref="FeedOptions" /> or <see cref="RequestOptions"/>
    /// parameters based on HTTP request headers, and we set the session consistency output header based on the response.
    /// </summary>
    internal class FhirDocumentClientGenerator : DocumentClientGenerator
    {
        internal override CSharpSyntaxRewriter CreateSyntaxRewriter(Compilation compilation, SemanticModel semanticModel)
        {
            return new ConsistencyLevelRewriter(compilation, semanticModel);
        }

        private class ConsistencyLevelRewriter : CSharpSyntaxRewriter
        {
            private readonly SemanticModel _semanticModel;
            private readonly INamedTypeSymbol _taskTypeSymbol;
            private readonly INamedTypeSymbol _taskOfTTypeSymbol;
            private readonly ImmutableHashSet<INamedTypeSymbol> _optionTypeSymbols;
            private readonly ImmutableHashSet<INamedTypeSymbol> _responseTypeSymbols;
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

                _taskTypeSymbol = compilation.GetTypeByMetadataName(typeof(Task).FullName);
                _taskOfTTypeSymbol = compilation.GetTypeByMetadataName(typeof(Task<>).FullName);
                _optionTypeSymbols = optionParameterTypes.Select(t => compilation.GetTypeByMetadataName(t.FullName)).ToImmutableHashSet();
                _responseTypeSymbols = new[] { typeof(DocumentResponse<>), typeof(FeedResponse<>), typeof(ResourceResponse<>), typeof(StoredProcedureResponse<>) }.Select(t => compilation.GetTypeByMetadataName(t.FullName)).ToImmutableHashSet();
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

                var returnTypeSymbol = _semanticModel.GetTypeInfo(node.ReturnType).Type as INamedTypeSymbol;

                bool isAsync = returnTypeSymbol != null &&
                               (returnTypeSymbol.Equals(_taskTypeSymbol) || returnTypeSymbol.ConstructedFrom.Equals(_taskOfTTypeSymbol));

                if (isAsync)
                {
                    visitedNode = visitedNode.AddModifiers(Token(SyntaxKind.AsyncKeyword));
                    var invocation = visitedNode.DescendantNodes().OfType<InvocationExpressionSyntax>().First();
                    visitedNode = visitedNode.ReplaceNode(invocation, AwaitExpression(invocation));
                }

                // wrap with try/catch

                visitedNode = visitedNode.WithBody(Block(TryStatement(
                    block: visitedNode.Body,
                    catches: SingletonList(
                        CatchClause()
                            .WithDeclaration(
                                CatchDeclaration(
                                        IdentifierName("System.Exception"))
                                    .WithIdentifier(
                                        Identifier("ex")))
                            .WithBlock(
                                Block(
                                    SeparatedList(
                                        new StatementSyntax[]
                                        {
                                            ExpressionStatement(InvocationExpression(IdentifierName("ProcessException")).AddArgumentListArguments(Argument(IdentifierName("ex")))),
                                            ThrowStatement(),
                                        })))),
                    @finally: null)));

                if (isAsync &&
                    returnTypeSymbol.TypeArguments.FirstOrDefault() is INamedTypeSymbol argumentType &&
                    argumentType.IsGenericType &&
                    _responseTypeSymbols.Contains(argumentType.ConstructedFrom))
                {
                    var awaitSyntax = visitedNode.DescendantNodes().OfType<AwaitExpressionSyntax>().First();
                    visitedNode = visitedNode.ReplaceNode(awaitSyntax, InvocationExpression(IdentifierName("ProcessResponse")).AddArgumentListArguments(Argument(awaitSyntax)));
                }

                return visitedNode;
            }
        }
    }
}
