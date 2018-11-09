// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Microsoft.Health.Extensions.BuildTimeCodeGenerator
{
    /// <summary>
    /// Generates a partial class for wrapping IDocumentClient, where for each call, wraps the call in try/catch block
    /// and handle exception as needed.
    /// </summary>
    internal class DocumentClientWithExceptionHandlerGenerator : DocumentClientGenerator
    {
        internal override SyntaxKind ConstructorModifier => SyntaxKind.PublicKeyword;

        internal override CSharpSyntaxRewriter CreateSyntaxRewriter(Compilation compilation, SemanticModel semanticModel)
        {
            return new ConsistencyLevelRewriter(compilation, semanticModel);
        }

        private class ConsistencyLevelRewriter : CSharpSyntaxRewriter
        {
            private readonly SemanticModel _semanticModel;
            private readonly INamedTypeSymbol _taskTypeSymbol;
            private readonly ImmutableHashSet<INamedTypeSymbol> _returnTypeSymbols;
            private readonly ImmutableHashSet<INamedTypeSymbol> _documentQueryTypesSymbol;

            public ConsistencyLevelRewriter(Compilation compilation, SemanticModel semanticModel)
            {
                // look for methods that rely on overloading instead of FeedOptions/RequestOptions being an optional parameter
                var returnTypes = new[]
                {
                    typeof(DocumentResponse<>),
                    typeof(FeedResponse<>),
                    typeof(ResourceResponse<>),
                    typeof(IStoredProcedureResponse<>),
                    typeof(MediaResponse),
                };

                _returnTypeSymbols = returnTypes.Select(t => compilation.GetTypeByMetadataName(t.FullName)).ToImmutableHashSet();
                _taskTypeSymbol = compilation.GetTypeByMetadataName(typeof(Task<>).FullName);

                var documentQueryTypes = new[]
                {
                    typeof(IDocumentQuery<>),
                };

                _documentQueryTypesSymbol = documentQueryTypes.Select(t => compilation.GetTypeByMetadataName(t.FullName)).ToImmutableHashSet();
                _semanticModel = semanticModel;
            }

            public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
            {
                var visitedNode = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node);

                if (_semanticModel.GetTypeInfo(node.ReturnType).Type is INamedTypeSymbol type &&
                    type.ConstructedFrom.Equals(_taskTypeSymbol) &&
                    type.TypeArguments[0] is INamedTypeSymbol argumentType &&
                    argumentType.IsGenericType &&
                    _returnTypeSymbols.Contains(argumentType.ConstructedFrom))
                {
                    visitedNode = visitedNode.AddModifiers(Token(SyntaxKind.AsyncKeyword));

                    var invocation = visitedNode.DescendantNodes().OfType<InvocationExpressionSyntax>().First();

                    TryStatementSyntax tryStatementSyntax = TryStatement(
                        Block(ReturnStatement(AwaitExpression(invocation))),
                        SingletonList<CatchClauseSyntax>(
                            CatchClause()
                            .WithDeclaration(
                                CatchDeclaration(
                                    IdentifierName("System.Exception"))
                                .WithIdentifier(
                                    Identifier("ex")))
                            .WithBlock(
                                Block(
                                    SeparatedList<StatementSyntax>(
                                        new StatementSyntax[]
                                        {
                                            ExpressionStatement(InvocationExpression(IdentifierName("ProcessException")).AddArgumentListArguments(Argument(IdentifierName("ex")))),
                                            ThrowStatement(),
                                        })))),
                        null);

                    visitedNode = visitedNode.WithBody(Block(tryStatementSyntax));

                    return visitedNode;
                }

                return visitedNode;
            }
        }
    }
}
