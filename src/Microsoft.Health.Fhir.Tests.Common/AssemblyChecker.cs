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
using Xunit.v3;

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

        /// <summary>
        /// Looks for collection definitions that carry traits and are shared with other classes.
        /// </summary>
        /// <remarks>
        /// A trait on a <c>[CollectionDefinition]</c> class is applied to every class that joins that
        /// collection, which is not obvious at either end: the trait is written in one file and takes
        /// effect in others, and nothing at the joining class says so. This repository's CI legs select
        /// on traits, so a collection that quietly adds a <c>Category</c> can move its members out of a
        /// leg that excludes that category - and a leg that runs fewer tests than it should still
        /// passes. That is not hypothetical; it is what this check was written after.
        /// <para>
        /// A definition that carries traits but that no other class joins is left alone: it applies
        /// them only to itself, which is the same as writing them on the class. The fix when this does
        /// fire is the pattern already used elsewhere here - move the collection name onto a separate,
        /// traitless definition class, and leave the traits on the test class that declared them.
        /// </para>
        /// </remarks>
        /// <param name="assembly">Assembly under analysis.</param>
        /// <returns>The offending definition types, each with the classes that join its collection.</returns>
        public static IReadOnlyList<(Type Definition, IReadOnlyList<Type> Members)> ScanForTraitCarryingSharedCollectionDefinitions(Assembly assembly)
        {
            EnsureArg.IsNotNull(assembly, nameof(assembly));

            Type[] classes = assembly.GetTypes().Where(t => t.IsClass).ToArray();

            var offenders = new List<(Type, IReadOnlyList<Type>)>();

            foreach (Type definition in classes)
            {
                CustomAttributeData definitionAttribute = definition.CustomAttributes
                    .FirstOrDefault(a => a.AttributeType == typeof(CollectionDefinitionAttribute));

                // Any ITraitAttribute propagates, not just the [Trait] spelling of one, so ask the
                // question xUnit asks rather than naming a single attribute type. The scan above is
                // deliberately narrower: it reads a trait's name out of the first constructor
                // argument, which is a shape only TraitAttribute guarantees.
                if (definitionAttribute == null ||
                    !definition.CustomAttributes.Any(a => typeof(ITraitAttribute).IsAssignableFrom(a.AttributeType)))
                {
                    continue;
                }

                // A collection definition can name its collection with a string or, since v3, be
                // referenced by its own type. Both spellings have to be matched to find the members.
                string collectionName = definitionAttribute.ConstructorArguments.Count > 0
                    ? definitionAttribute.ConstructorArguments[0].Value as string
                    : null;

                List<Type> members = classes
                    .Where(candidate => candidate != definition && JoinsCollection(candidate, collectionName, definition))
                    .ToList();

                if (members.Count > 0)
                {
                    offenders.Add((definition, members));
                }
            }

            return offenders;
        }

        private static bool JoinsCollection(Type candidate, string collectionName, Type definition)
        {
            foreach (CustomAttributeData attribute in candidate.CustomAttributes.Where(a => a.AttributeType == typeof(CollectionAttribute)))
            {
                if (attribute.ConstructorArguments.Count == 0)
                {
                    continue;
                }

                object value = attribute.ConstructorArguments[0].Value;

                if (value is string name && collectionName != null && string.Equals(name, collectionName, StringComparison.Ordinal))
                {
                    return true;
                }

                if (value is Type referenced && referenced == definition)
                {
                    return true;
                }
            }

            return false;
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
