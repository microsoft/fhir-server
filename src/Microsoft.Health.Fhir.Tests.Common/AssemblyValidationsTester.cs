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
