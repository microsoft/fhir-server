// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;
using IDocumentClient = Microsoft.Azure.Documents.IDocumentClient;

namespace Microsoft.Health.Extensions.BuildTimeCodeGenerator.CosmosDb
{
    internal abstract class DocumentClientGenerator : ICodeGenerator
    {
        internal DocumentClientGenerator()
        {
        }

        internal virtual SyntaxKind ConstructorModifier => SyntaxKind.PrivateKeyword;

        internal abstract CSharpSyntaxRewriter CreateSyntaxRewriter(Compilation compilation, SemanticModel semanticModel);

        (MemberDeclarationSyntax, UsingDirectiveSyntax[]) ICodeGenerator.Generate(string typeName)
        {
            // First generate a basic class that implements the interfaces and delegates to an inner field.
            var generator = new DelegatingInterfaceImplementationGenerator(
                typeModifiers: SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.InternalKeyword), SyntaxFactory.Token(SyntaxKind.PartialKeyword)),
                constructorModifiers: SyntaxFactory.TokenList(SyntaxFactory.Token(ConstructorModifier)),
                typeof(IDocumentClient),
                typeof(IDisposable));

            (MemberDeclarationSyntax declaration, UsingDirectiveSyntax[] usings) = generator.Generate(typeName);

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

            var rewriter = CreateSyntaxRewriter(compilation, compilation.GetSemanticModel(syntaxTree));
            var rewrittenCompilationUnit = (CompilationUnitSyntax)rewriter.Visit(syntaxTree.GetRoot());
            return (rewrittenCompilationUnit.Members.Single(), usings);
        }
    }
}
