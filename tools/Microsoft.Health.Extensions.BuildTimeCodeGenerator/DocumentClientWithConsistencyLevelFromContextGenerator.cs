// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
    internal class DocumentClientWithConsistencyLevelFromContextGenerator : ICodeGenerator
    {
        public SyntaxNode Generate(string namespaceName, string typeName, Compilation compilation)
        {
            var generator = new DelegatingInterfaceImplementationGenerator(
                typeModifiers: TokenList(Token(SyntaxKind.InternalKeyword), Token(SyntaxKind.PartialKeyword)),
                constructorModifiers: TokenList(Token(SyntaxKind.PrivateKeyword)),
                typeof(IDocumentClient),
                typeof(IDisposable));

            var syntaxNode = generator.Generate(namespaceName, typeName, compilation);

            compilation = compilation.AddSyntaxTrees(syntaxNode.SyntaxTree);

            var rewriter = new ConsistencyLevelRewriter(compilation, compilation.GetSemanticModel(syntaxNode.SyntaxTree));
            return rewriter.Visit(syntaxNode);
        }

        private class ConsistencyLevelRewriter : CSharpSyntaxRewriter
        {
            private readonly SemanticModel _semanticModel;
            private readonly INamedTypeSymbol _taskType;
            private readonly INamedTypeSymbol[] _optionTypes;
            private readonly INamedTypeSymbol[] _responseTypes;

            public ConsistencyLevelRewriter(Compilation compilation, SemanticModel semanticModel)
            {
                _taskType = compilation.GetTypeByMetadataName(typeof(Task<>).FullName);

                _optionTypes = new[] { typeof(FeedOptions), typeof(RequestOptions), typeof(ResourceResponse<>), typeof(StoredProcedureResponse<>) }.Select(t => compilation.GetTypeByMetadataName(t.FullName)).ToArray();
                _responseTypes = new[] { typeof(DocumentResponse<>), typeof(FeedResponse<>), typeof(ResourceResponse<>), typeof(StoredProcedureResponse<>) }.Select(t => compilation.GetTypeByMetadataName(t.FullName)).ToArray();
                _semanticModel = semanticModel;
            }

            public override SyntaxNode VisitArgument(ArgumentSyntax node)
            {
                ITypeSymbol type = _semanticModel.GetTypeInfo(node.Expression).Type;

                if (_optionTypes.Contains(type))
                {
                    return node.WithExpression(InvocationExpression(IdentifierName("UpdateOptions")).AddArgumentListArguments(node));
                }

                return base.VisitArgument(node);
            }

            public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
            {
                var visitedNode = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node);

                if (_semanticModel.GetTypeInfo(node.ReturnType).Type is INamedTypeSymbol type &&
                    type.ConstructedFrom.Equals(_taskType) &&
                    type.TypeArguments[0] is INamedTypeSymbol argumentType &&
                    argumentType.IsGenericType &&
                    _responseTypes.Contains(argumentType.ConstructedFrom))
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
