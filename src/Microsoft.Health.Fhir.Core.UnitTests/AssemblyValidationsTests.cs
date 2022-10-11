// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests
{
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
    }
}
