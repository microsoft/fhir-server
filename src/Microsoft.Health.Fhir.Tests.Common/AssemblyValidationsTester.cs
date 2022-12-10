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
