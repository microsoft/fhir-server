// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FhirAnalyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class LogInterpolation : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "StringInterpolationInLogger";

        private static readonly LocalizableString Title = "String interpolation in logger method";
        private static readonly LocalizableString MessageFormat = "Found string interpolation in logger method which can impend structural logging. Please rewrite code to pass string and parameters for it.";
        private static readonly LocalizableString Description = "Logging doesn't work well with string interpolation, it's better to rewrite code to string.Format message as first one and pass all parameters in the end.";

        private const string Category = "Logging";

        private static readonly HashSet<string> MethodNames = new HashSet<string>() { "Log", "LogCritical", "LogDebug", "LogError", "LogInformational", "LogTrace", "LogWarning" };

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get { return ImmutableArray.Create(Rule); }
        }

        public override void Initialize(AnalysisContext context)
        {
#pragma warning disable CA1062 // Validate arguments of public methods
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
#pragma warning restore CA1062 // Validate arguments of public methods
            context.EnableConcurrentExecution();

            context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.InvocationExpression);
        }

        private static void AnalyzeNode(SyntaxNodeAnalysisContext context)
        {
            var syntax = (InvocationExpressionSyntax)context.Node;
            var methodName = syntax.Expression.TryGetInferredMemberName();

            // Check method name.
            if (methodName == null || !MethodNames.Contains(methodName))
            {
                return;
            }

            // Check namespace of the logger object.
            var space = context.SemanticModel.GetSymbolInfo(syntax.Expression).Symbol;
            if (!space.ToString().StartsWith("Microsoft.Extensions.Logging.ILogger", System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Log methods can have string message in different places as argument, so let's find that argument!
            ArgumentSyntax messageArgument = null;
            foreach (var argument in syntax.ArgumentList.Arguments)
            {
                var type = context.SemanticModel.GetTypeInfo(argument.ChildNodes().First()).Type;
                if (type != null && type.ToString() == "string")
                {
                    messageArgument = argument;
                    break;
                }
            }

            if (messageArgument == null)
            {
                return;
            }

            if (messageArgument.Expression is InterpolatedStringExpressionSyntax argumentSyntax)
            {
                bool violate = false;

                // we inside message, and it's interpolated string. Let's see what it compose of.
                foreach (var content in argumentSyntax.Contents)
                {
                    // it's ok to pass `nameof(A)` because it's basically a constant and if there is no other variables,
                    // string we interpolated would be constant string as well.
                    if (!((content is InterpolationSyntax interpolation &&
                        interpolation.Expression is InvocationExpressionSyntax invocationSyntax &&
                        invocationSyntax.Expression is IdentifierNameSyntax nameSyntax &&
                        nameSyntax.Identifier.ValueText.Equals("nameof", System.StringComparison.OrdinalIgnoreCase))

                        // it's ok to have string itself
                        || content.Kind() == SyntaxKind.InterpolatedStringText))
                    {
                        // everything else means we have variable string, and we don't want that.
                        violate = true;
                        break;
                    }
                }

                if (violate)
                {
                    var diagnostic = Diagnostic.Create(Rule, syntax.GetLocation(), space, methodName);

                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }
}
