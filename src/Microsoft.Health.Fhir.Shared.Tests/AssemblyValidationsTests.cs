// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Tests
{
    /// <summary>
    /// Assembly-wide checks, from the shared project the unit, integration, and E2E assemblies all
    /// compile.
    /// </summary>
    /// <remarks>
    /// This copy deliberately carries no <c>DataStore</c> trait. It compiles into the integration
    /// assemblies, whose legs select by <em>excluding</em> the other store, and an exclusion drops a
    /// case when any of its values under that trait matches - so naming both stores here would hide
    /// these checks from both integration legs rather than reveal them. The consequence is that in
    /// an E2E assembly this copy is invisible to the positively-filtered legs, and a check added
    /// only here would not run there. E2E-visible checks belong in the copy under
    /// <c>Microsoft.Health.Fhir.Shared.Tests.E2E</c>, which is traited for exactly that reason.
    /// </remarks>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.AssemblyValidation)]
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
