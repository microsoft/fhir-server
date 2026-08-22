// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Common
{
    public static class AssemblyValidationsTester
    {
        public static void EnsureAllTestsHaveCategoryTrait(Assembly assembly)
        {
            var types = AssemblyChecker.ScanTestsLookingForMissingTrait(assembly, Traits.Category);
            AssertTestClasses(assembly, Traits.Category, types);
        }

        public static void EnsureAllTestsHaveOwningTeamTrait(Assembly assembly)
        {
            var types = AssemblyChecker.ScanTestsLookingForMissingTrait(assembly, Traits.OwningTeam);
            AssertTestClasses(assembly, Traits.OwningTeam, types);
        }

        /// <summary>
        /// Fails when a collection definition carrying traits is shared with other classes, because
        /// those classes silently acquire the traits and the CI legs select on them.
        /// </summary>
        /// <param name="assembly">Assembly under analysis.</param>
        public static void EnsureNoSharedCollectionDefinitionCarriesTraits(Assembly assembly)
        {
            IReadOnlyList<(Type Definition, IReadOnlyList<Type> Members)> offenders =
                AssemblyChecker.ScanForTraitCarryingSharedCollectionDefinitions(assembly);

            if (offenders.Count == 0)
            {
                return;
            }

            var stringBuilder = new StringBuilder();
            foreach ((Type definition, IReadOnlyList<Type> members) in offenders)
            {
                stringBuilder.AppendLine($"{definition} applies its traits to: {string.Join(", ", members.Select(m => m.ToString()))}");
            }

            Assert.Fail(
                $"Assembly '{assembly}' has collection definitions whose traits are applied to other classes. Those classes gain traits nothing at their declaration mentions, " +
                $"and a CI leg selecting on traits will then run a different set of tests than it appears to - without failing. Move the collection name onto a separate traitless " +
                $"definition class and leave the traits where they were declared.{Environment.NewLine}{stringBuilder}");
        }

        /// <summary>
        /// Fails when the assembly does not declare the test framework its tests are written for.
        /// </summary>
        /// <param name="assembly">Assembly under analysis.</param>
        /// <param name="expectedFrameworkType">The test framework the assembly must declare.</param>
        /// <remarks>
        /// The custom framework is what expands fixture argument sets into variants, and the variants
        /// are what carry the <c>DataStore</c> trait every E2E and export leg selects positively on.
        /// Lose the declaration and nothing throws: the classes are discovered as ordinary xUnit
        /// tests, carry no data store trait, and are filtered out of every leg. A class that is
        /// filtered out never constructs its fixture, so no connection is attempted and nothing
        /// errors - the leg runs whatever handful of tests declared their traits by hand, reports
        /// success, and the entire suite is missing. This check turns that into a failure.
        /// </remarks>
        public static void EnsureTestFrameworkIsDeclared(Assembly assembly, Type expectedFrameworkType)
        {
            TestFrameworkAttribute[] declared = assembly.GetCustomAttributes<TestFrameworkAttribute>().ToArray();

            if (declared.Length == 1 && declared[0].FrameworkType == expectedFrameworkType)
            {
                return;
            }

            string found = declared.Length == 0
                ? "none"
                : string.Join(", ", declared.Select(attribute => attribute.FrameworkType?.FullName ?? "<null>"));

            Assert.Fail(
                $"Assembly '{assembly}' must declare exactly one test framework, '{expectedFrameworkType.FullName}', but declares: {found}. " +
                $"Without it the fixture argument sets are never expanded, so the tests carry no data store trait, every leg that selects positively on one " +
                $"filters them out, and the leg passes with the suite missing rather than failing.");
        }

        private static void AssertTestClasses(Assembly currentAssembly, string traitName, IEnumerable<Type> types)
        {
            if (types == null || !types.Any())
            {
                return;
            }

            StringBuilder stringBuilder = new StringBuilder();
            foreach (Type type in types)
            {
                stringBuilder.AppendLine(type.ToString());
            }

            Assert.Fail($"Assembly '{currentAssembly}' is not compliant, because not all Test Classes have Trait '{traitName}'. Classes: {Environment.NewLine}{stringBuilder.ToString()}");
        }
    }
}
