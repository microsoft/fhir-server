// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using Microsoft.Health.Extensions.Xunit;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E
{
    /// <summary>
    /// Assembly-wide checks over the E2E tests.
    /// </summary>
    /// <remarks>
    /// Every E2E leg selects positively on a data store - <c>/[(DataStore=CosmosDb)&amp;...]</c> - and
    /// a positive match cannot select a test case that carries no <c>DataStore</c> trait at all. Real
    /// E2E classes get theirs from <c>HttpIntegrationFixtureArgumentSets</c>, but this class has no
    /// server fixture and wants none: it only reads the assembly's metadata. Without the traits below
    /// it is filtered out of every leg and never runs anywhere, which would leave these checks - one
    /// of which exists to catch the collection-trait propagation that hid twelve E2E cases behind a
    /// green run - quietly inert, in the one assembly the incident came from. Both stores are named
    /// so the checks run whichever leg is executing.
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

        [Fact]
        public void GivenCurrentAssembly_WhenScanned_EnsureTheCustomTestFrameworkIsDeclared()
        {
            AssemblyValidationsTester.EnsureTestFrameworkIsDeclared(_currentAssembly, typeof(CustomXunitTestFramework));
        }
    }
}
