// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EnsureThat;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Common
{
    public static class AssemblyChecker
    {
        /// <summary>
        /// Look for test classes not compliance with a Trait.
        /// </summary>
        /// <param name="assembly">Assembly under analysis</param>
        /// <param name="traitName">Name of the trait being scanned</param>
        /// <returns>List of not compliance test classes.</returns>
        public static IEnumerable<Type> ScanTestsLookingForMissingTrait(Assembly assembly, string traitName)
        {
            EnsureArg.IsNotNull(assembly, nameof(assembly));
            EnsureArg.IsNotNullOrWhiteSpace(traitName, nameof(assembly));

            IEnumerable<Type> assemblyClasses = assembly.GetTypes().Where(t => t.IsClass);

            List<Type> notComplianceTestClass = new List<Type>();

            foreach (Type assemblyClass in assemblyClasses)
            {
                if (IsTestClass(assemblyClass))
                {
                    IEnumerable<CustomAttributeData> traitsAttributes = assemblyClass.CustomAttributes.Where(a => a.AttributeType == typeof(TraitAttribute));
                    if (!traitsAttributes.Any())
                    {
                        // Class does not contain any Trait.
                        notComplianceTestClass.Add(assemblyClass);
                        continue;
                    }

                    bool containsOwningTeam = traitsAttributes.Any(a => string.Equals(a.ConstructorArguments[0].Value, traitName));
                    if (!containsOwningTeam)
                    {
                        notComplianceTestClass.Add(assemblyClass);
                    }
                }
            }

            return notComplianceTestClass;
        }

        private static bool IsTestClass(Type type)
        {
            if (!type.IsClass)
            {
                return false;
            }

            MethodInfo[] methods = type.GetMethods();
            foreach (MethodInfo method in methods)
            {
                IEnumerable<Attribute> attributes = method.GetCustomAttributes();
                if (attributes.Any(a => a is TheoryAttribute || a is FactAttribute))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
