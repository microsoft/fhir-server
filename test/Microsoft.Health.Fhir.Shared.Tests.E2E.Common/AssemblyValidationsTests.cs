// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Health.Extensions.Xunit;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Tests.E2E.Common
{
    /// <summary>
    /// Assembly-wide checks over the E2E tests, from the shared project the E2E assemblies alone
    /// compile.
    /// </summary>
    /// <remarks>
    /// This project is imported by the four E2E assemblies and nothing else, and every E2E leg
    /// selects positively on a data store, so without the traits below this copy is filtered out of
    /// every leg and never runs. It would still compile, and a check added to it would still look
    /// like it was covering the E2E assemblies while running nowhere. Naming both stores is safe
    /// here precisely because only positively-filtered legs compile it: a leg that selected by
    /// <em>excluding</em> a store would drop a class naming both, which is why the copy shared with
    /// the integration assemblies deliberately carries neither.
    /// </remarks>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.AssemblyValidation)]
    [Trait(nameof(DataStore), nameof(DataStore.CosmosDb))]
    [Trait(nameof(DataStore), nameof(DataStore.SqlServer))]
    public sealed class AssemblyValidationsTests
    {
        private static Assembly _currentAssembly = Assembly.GetAssembly(typeof(AssemblyValidationsTests));

        private static readonly HashSet<string> KnownUnreachableTestClasses = new HashSet<string>(StringComparer.Ordinal)
        {
            // Untraited on purpose. This copy is also compiled into the integration assemblies, whose
            // legs filter by excluding a store rather than naming one, and an exclusion drops a class
            // that names both. Traiting it here would silence it there, so the E2E copy above carries
            // the traits instead and this one runs in the integration legs only.
            "Microsoft.Health.Fhir.Shared.Tests.AssemblyValidationsTests",

            // Pre-existing, and unrun before this PR as well: the legs used to select by name
            // substring, which these matched no better than they match the trait filters. They need a
            // container registry and a convert-data configuration that no leg sets up, so traiting
            // them would move them from never running to reliably failing.
            "Microsoft.Health.Fhir.Tests.E2E.Rest.ContainerRegistryTemplateUploaderTests",
            "Microsoft.Health.Fhir.Tests.E2E.Rest.ConvertDataTestModeTests",

            // Takes its data store as an [InlineData] argument, which is a value and not a trait, so
            // no filter can see it. Traiting the class would run every row in both legs, including
            // the row written for the other store, so this needs per-row traits rather than a
            // one-line fix.
            "Microsoft.Health.Fhir.Tests.E2E.Rest.Search.SearchParameterInitializationTests",
        };

        /// <summary>
        /// Methods in a class that only a sibling's argument sets make visible to the class-level
        /// check. Empty, and meant to stay so: an entry here is a test nobody runs.
        /// </summary>
        private static readonly HashSet<string> KnownUnreachableTestMethods = new HashSet<string>(StringComparer.Ordinal);

        [Fact]
        public void GivenCurrentAssembly_WhenScanned_EnsureAllTestsHaveCategoryTrait()
        {
            AssemblyValidationsTester.EnsureAllTestsHaveCategoryTrait(_currentAssembly);
        }

        [Fact]
        public void GivenCurrentAssembly_WhenScanned_EnsureAllTestsHaveOwningTeamTrait()
        {
            AssemblyValidationsTester.EnsureAllTestsHaveOwningTeamTrait(_currentAssembly);
        }

        [Fact]
        public void GivenCurrentAssembly_WhenScanned_EnsureNoSharedCollectionDefinitionCarriesTraits()
        {
            AssemblyValidationsTester.EnsureNoSharedCollectionDefinitionCarriesTraits(_currentAssembly);
        }

        /// <summary>
        /// Every leg selects positively on <see cref="DataStore"/>, so a class that carries no such
        /// trait runs nowhere while every leg stays green. The classes below are in that state
        /// already, from before the legs filtered on traits at all; they are listed so the situation
        /// is visible and cannot quietly grow.
        /// </summary>
        [Fact]
        public void GivenCurrentAssembly_WhenScanned_EnsureEveryTestClassIsSelectedBySomeLeg()
        {
            AssemblyValidationsTester.EnsureEveryTestClassIsSelectedBySomeLeg(
                _currentAssembly,
                nameof(DataStore),
                typeof(FixtureArgumentSetsAttribute),
                KnownUnreachableTestClasses);
        }

        /// <summary>
        /// A single decorated method exempts its whole class from the check above, so its
        /// undecorated siblings would be selected by no leg and reported by nothing.
        /// </summary>
        [Fact]
        public void GivenCurrentAssembly_WhenScanned_EnsureEveryTestMethodIsSelectedBySomeLeg()
        {
            AssemblyValidationsTester.EnsureEveryTestMethodIsSelectedBySomeLeg(
                _currentAssembly,
                nameof(DataStore),
                typeof(FixtureArgumentSetsAttribute),
                KnownUnreachableTestMethods);
        }
    }
}
