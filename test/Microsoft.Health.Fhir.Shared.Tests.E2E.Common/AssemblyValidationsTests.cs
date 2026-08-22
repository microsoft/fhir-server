// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
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
    }
}
