// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.FhirPath;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa.FhirPath;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Search.FhirPath
{
    /// <summary>
    /// Exercises <see cref="IgnixaFhirPathEvaluator"/> the way the server actually uses it: as a singleton
    /// driven concurrently by import workers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The evaluator holds one shared <c>FhirPathParser</c>, <c>FhirPathEvaluator</c> and
    /// <c>FhirPathDelegateCompiler</c>, and <c>ConcurrentDictionary.GetOrAdd</c> does not serialise its value
    /// factory, so compilation of different expressions genuinely runs those shared objects in parallel.
    /// Evaluation does too. Nothing in the Ignixa package documents whether that is supported, and search
    /// indexing has no runtime fallback to Firely, so a data race here would surface as intermittent, load
    /// dependent index corruption rather than a clean failure.
    /// </para>
    /// <para>
    /// These tests exist so that assumption is verified rather than believed. They compare every concurrent
    /// result against the single-threaded Firely result for the same expression, so torn state shows up as a
    /// value mismatch and not merely as an absence of exceptions.
    /// </para>
    /// </remarks>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class IgnixaFhirPathEvaluatorConcurrencyTests
    {
        private const int DegreeOfParallelism = 16;
        private const int IterationsPerThread = 40;

        private static readonly DateTimeOffset LastModified = new(1990, 1, 2, 3, 4, 5, 123, TimeSpan.Zero);

        private static readonly string[] Expressions =
        {
            "Patient.id",
            "Patient.active",
            "Patient.name",
            "Patient.name.family",
            "Patient.name.given",
            "Patient.birthDate",
            "Patient.gender",
            "Patient.telecom",
            "Patient.telecom.where(system='phone')",
            "Patient.address.city",
            "Patient.address.postalCode",
            "Patient.identifier",
            "Patient.managingOrganization",
            "Patient.name.where(use='official').family",
            "Patient.name.exists()",
            "Patient.telecom.count()",
            "Patient.meta.lastUpdated",
        };

        static IgnixaFhirPathEvaluatorConcurrencyTests()
        {
            FhirPathCompiler.DefaultSymbolTable.AddFhirExtensions();
        }

        [Fact]
        public async Task GivenASharedEvaluator_WhenManyThreadsCompileAndEvaluate_ThenEveryResultMatchesFirely()
        {
            // One evaluator for every thread, exactly as the singleton registration in SearchModule produces.
            var evaluator = new IgnixaFhirPathEvaluator();

            Dictionary<string, string[]> expected = BuildExpectedResults();

            var failures = new ConcurrentBag<string>();

            await Parallel.ForEachAsync(
                Enumerable.Range(0, DegreeOfParallelism),
                new ParallelOptions { MaxDegreeOfParallelism = DegreeOfParallelism },
                (worker, _) =>
                {
                    try
                    {
                        for (int iteration = 0; iteration < IterationsPerThread; iteration++)
                        {
                            // A distinct resource instance per iteration, as each imported NDJSON line would be.
                            ITypedElement element = Parse(PatientJson(worker, iteration));
                            var context = new EvaluationContext { Resource = element, RootResource = element };

                            foreach (string expression in Expressions)
                            {
                                string[] actual = evaluator.Compile(expression)
                                    .Evaluate(element, context)
                                    .Select(Describe)
                                    .ToArray();

                                // Patient.id is the one value that differs per iteration; comparing it against
                                // this worker's own id is what would catch a thread reading another thread's
                                // resource. Everything else is identical across resources, so it compares
                                // against the Firely baseline.
                                string[] want = string.Equals(expression, "Patient.id", StringComparison.Ordinal)
                                    ? new[] { $"id=patient-{worker}-{iteration}" }
                                    : expected[expression];

                                if (!actual.SequenceEqual(want, StringComparer.Ordinal))
                                {
                                    failures.Add(
                                        $"worker {worker} iteration {iteration} '{expression}': expected [{string.Join("|", want)}] but got [{string.Join("|", actual)}]");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"worker {worker} threw {ex.GetType().Name}: {ex.Message}");
                    }

                    return ValueTask.CompletedTask;
                });

            Assert.True(failures.IsEmpty, string.Join(Environment.NewLine, failures.Take(10)));
        }

        [Fact]
        public async Task GivenAColdEvaluator_WhenManyThreadsCompileTheSameExpressionsAtOnce_ThenCompilationIsRaceFree()
        {
            // Targets the compile path specifically: every thread starts against an empty cache, so the
            // ConcurrentDictionary value factory - and therefore the shared parser and delegate compiler - run
            // concurrently for the same and for different expressions.
            var evaluator = new IgnixaFhirPathEvaluator();
            var failures = new ConcurrentBag<string>();

            await Parallel.ForEachAsync(
                Enumerable.Range(0, DegreeOfParallelism),
                new ParallelOptions { MaxDegreeOfParallelism = DegreeOfParallelism },
                (worker, _) =>
                {
                    try
                    {
                        // Reversing on alternate workers makes threads collide on different expressions first.
                        IEnumerable<string> order = worker % 2 == 0 ? Expressions : Expressions.Reverse();

                        foreach (string expression in order)
                        {
                            ICompiledFhirPath compiled = evaluator.Compile(expression);

                            if (!string.Equals(compiled.Expression, expression, StringComparison.Ordinal))
                            {
                                failures.Add($"worker {worker}: asked for '{expression}' but got '{compiled.Expression}'");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"worker {worker} threw {ex.GetType().Name}: {ex.Message}");
                    }

                    return ValueTask.CompletedTask;
                });

            Assert.True(failures.IsEmpty, string.Join(Environment.NewLine, failures.Take(10)));
        }

        private static Dictionary<string, string[]> BuildExpectedResults()
        {
            var firely = new FirelyFhirPathEvaluator();
            ITypedElement element = Parse(PatientJson(0, 0));
            var context = new EvaluationContext { Resource = element, RootResource = element };

            return Expressions.ToDictionary(
                expression => expression,
                expression => firely.Compile(expression).Evaluate(element, context).Select(Describe).ToArray(),
                StringComparer.Ordinal);
        }

        private static ITypedElement Parse(string json)
        {
            var rawResource = new RawResource(json, FhirResourceFormat.Json, isMetaSet: false);

            // A fixed timestamp, not UtcNow: DeserializeRaw stamps meta.lastUpdated with the value passed here,
            // so using the clock would make every parse differ and the comparison meaningless.
            return Deserializers.ResourceDeserializer
                .DeserializeRaw(rawResource, "1", LastModified)
                .Instance;
        }

        private static string Describe(ITypedElement element)
        {
            if (element.Value != null)
            {
                return $"{element.InstanceType}={Convert.ToString(element.Value, CultureInfo.InvariantCulture)}";
            }

            return $"{element.InstanceType}({string.Join(",", element.Children().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal))})";
        }

        /// <summary>
        /// Builds a resource whose identity varies per worker and iteration, so a thread reading another
        /// thread's state produces a value mismatch rather than coincidentally matching.
        /// </summary>
        private static string PatientJson(int worker, int iteration) => $$"""
            {
              "resourceType": "Patient",
              "id": "patient-{{worker}}-{{iteration}}",
              "meta": { "versionId": "3", "lastUpdated": "1990-01-02T03:04:05.123+00:00" },
              "identifier": [ { "system": "http://example.org/id", "value": "id-0" } ],
              "active": true,
              "name": [
                { "use": "official", "family": "Chalmers", "given": [ "Peter", "James" ] },
                { "use": "usual", "given": [ "Jim" ] }
              ],
              "telecom": [ { "system": "phone", "value": "555-1234", "use": "home" } ],
              "gender": "male",
              "birthDate": "1974-12-25",
              "address": [ { "city": "PleasantVille", "state": "Vic", "postalCode": "3999" } ],
              "managingOrganization": { "reference": "Organization/1" }
            }
            """;
    }
}
