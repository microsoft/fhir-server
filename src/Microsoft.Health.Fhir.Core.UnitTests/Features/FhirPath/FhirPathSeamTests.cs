// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.FhirPath
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class FhirPathSeamTests
    {
        private static readonly HashSet<string> AllowedFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "src/Microsoft.Health.Fhir.Core/Features/FhirPath/FhirPathExtensions.cs",
            "src/Microsoft.Health.Fhir.Core/Features/FhirPath/FirelyCompiledFhirPath.cs",
            "src/Microsoft.Health.Fhir.Core/Features/FhirPath/FirelyFhirPathProvider.cs",
            "src/Microsoft.Health.Fhir.Core/Features/FhirPath/ICompiledFhirPath.cs",

            // The composition root unconditionally invokes Firely's guarded, idempotent registration
            // because FHIRPath Patch remains Firely-backed when the evaluation provider is Ignixa.
            "src/Microsoft.Health.Fhir.Shared.Api/Modules/SearchModule.cs",

            // FHIRPath Patch mutates the selected Firely ElementNode instances. Ignixa adapters cannot
            // preserve that node identity, so Patch remains Firely-backed until migration phase 7.
            "src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Patch/FhirPathPatch/Operations/OperationAdd.cs",
            "src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Patch/FhirPathPatch/Operations/OperationDelete.cs",
            "src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Patch/FhirPathPatch/Operations/OperationInsert.cs",
            "src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Patch/FhirPathPatch/Operations/OperationMove.cs",
            "src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Patch/FhirPathPatch/Operations/OperationReplace.cs",
            "src/Microsoft.Health.Fhir.Shared.Core/Features/Resources/Patch/FhirPathPatch/Operations/OperationUpsert.cs",

            // These consumers inspect Firely's AST and never evaluate an expression.
            "src/Microsoft.Health.Fhir.Shared.Core/Features/Search/Parameters/SearchParameterComparer.cs",
            "src/Microsoft.Health.Fhir.Shared.Core/Features/Search/Parameters/SearchParameterSupportResolver.cs",
            "src/Microsoft.Health.Fhir.Shared.Core/Features/Search/Parameters/SearchParameterToTypeResolver.cs",
        };

        [Fact]
        public void GivenProductionSource_WhenEngineNamespacesAreImported_ThenOnlyDocumentedExceptionsRemain()
        {
            string root = FindRepositoryRoot();
            string[] sourceRoots =
            [
                Path.Join(root, "src"),
                Path.Join(root, "tools", "Microsoft.Health.Fhir.R4.ResourceParser"),
            ];

            string[] violations = sourceRoots.SelectMany(sourceRoot => Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.Contains("UnitTests", StringComparison.OrdinalIgnoreCase))
                .Where(ImportsFirelyEngine)
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
                .Where(path => !AllowedFiles.Contains(path))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            Assert.True(violations.Length == 0, $"Direct Firely FHIRPath imports bypass the provider seam:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
        }

        private static bool ImportsFirelyEngine(string path)
        {
            string source = File.ReadAllText(path);
            return Regex.IsMatch(
                    source,
                    @"using\s+(?:\w+\s*=\s*)?(?:global::)?Hl7\.FhirPath\s*;",
                    RegexOptions.CultureInvariant) ||
                Regex.IsMatch(
                    source,
                    @"using\s+(?:\w+\s*=\s*)?(?:global::)?Hl7\.Fhir\.FhirPath\s*;",
                    RegexOptions.CultureInvariant) ||
                Regex.IsMatch(
                    source,
                    @"(?:global::)?Hl7\.FhirPath\.(?!(?:Expressions|Sprache|EvaluationContext)\b)",
                    RegexOptions.CultureInvariant) ||
                Regex.IsMatch(
                    source,
                    @"(?:global::)?Hl7\.Fhir\.FhirPath\.(?!FhirEvaluationContext\b)",
                    RegexOptions.CultureInvariant);
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Microsoft.Health.Fhir.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
        }
    }
}
