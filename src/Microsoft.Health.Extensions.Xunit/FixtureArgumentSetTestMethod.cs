// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EnsureThat;
using Xunit.v3;

namespace Microsoft.Health.Extensions.Xunit
{
    internal sealed class FixtureArgumentSetTestMethod : XunitTestMethod
    {
        private static readonly FieldInfo TraitsField = GetRequiredField("traits");

        private readonly IReadOnlyList<SingleFlag> _fixtureArguments;

        public FixtureArgumentSetTestMethod(FixtureArgumentSetTestClass testClass, MethodInfo methodInfo, IReadOnlyList<SingleFlag> fixtureArguments, string uniqueId)
            : base(testClass, methodInfo, testMethodArguments: BuildMethodArguments(fixtureArguments), uniqueId)
        {
            EnsureArg.IsNotNull(testClass, nameof(testClass));
            EnsureArg.IsNotNull(methodInfo, nameof(methodInfo));
            EnsureArg.IsNotNull(fixtureArguments, nameof(fixtureArguments));

            _fixtureArguments = fixtureArguments;
        }

#pragma warning disable CS0618 // Called by the de-serializer; should only be called by deriving classes for de-serialization purposes
        public FixtureArgumentSetTestMethod()
        {
            // The de-serializer bypasses the constructor that assigns this field, and the argument set
            // values are recovered from the base type's method arguments rather than from it. Leaving
            // it null would turn any later read into a NullReferenceException reported as a test
            // failure with no bearing on the test, so it starts empty as it does on the class.
            _fixtureArguments = Array.Empty<SingleFlag>();
        }
#pragma warning restore CS0618

        /// <summary>
        /// Resolves a private field on <see cref="XunitTestMethod"/> that this type has to write to.
        /// </summary>
        /// <remarks>
        /// xUnit v3 holds this state in private fields with no public setter and no virtual member
        /// that would let a derived type supply it, so reflection is the only way to set it. The
        /// members are not virtual either, which is why hiding them with <c>new</c> does not work:
        /// xUnit dispatches through <see cref="IXunitTestMethod"/> and would never see the override.
        /// </remarks>
        /// <param name="name">The name of the private instance field.</param>
        /// <returns>The field.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the field no longer exists.</exception>
        private static FieldInfo GetRequiredField(string name)
        {
            return typeof(XunitTestMethod).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    $"XunitTestMethod.{name} was not found. Fixture argument set traits and arguments cannot be applied, " +
                    "which would silently change which tests the CI filters select. This usually means the xunit.v3 version changed.");
        }

        /// <summary>
        /// Builds the arguments the base type is constructed with, which are the fixture's argument
        /// set values.
        /// </summary>
        /// <remarks>
        /// These are supplied through the base constructor rather than written back over it
        /// afterwards, so that the private field holding them is one less thing this type has to
        /// reach into and one less way a change to xunit can go unnoticed.
        /// </remarks>
        /// <param name="fixtureArguments">The argument set values this variant stands for.</param>
        /// <returns>The values as an argument array.</returns>
        private static object[] BuildMethodArguments(IReadOnlyList<SingleFlag> fixtureArguments)
        {
            EnsureArg.IsNotNull(fixtureArguments, nameof(fixtureArguments));

            if (fixtureArguments.Count == 0)
            {
                return Array.Empty<object>();
            }

            var result = new object[fixtureArguments.Count];
            for (int i = 0; i < fixtureArguments.Count; i++)
            {
                result[i] = fixtureArguments[i].EnumValue;
            }

            return result;
        }

        /// <summary>
        /// Adds a trait naming each fixture argument set value this variant stands for, so a CI leg
        /// selecting on those traits can see it.
        /// </summary>
        /// <remarks>
        /// A value here is never null: the only producer builds every <see cref="SingleFlag"/> through
        /// its constructor, and a degenerate combination is rejected before reaching this point. It is
        /// deliberately not guarded against anyway. Skipping a value that somehow was null would drop
        /// the trait and leave this variant unselectable by the E2E and export legs, which each require
        /// a positive match - the test would vanish from them silently. Throwing instead surfaces as a
        /// discovery fault, which this assembly turns into a reported failure per argument set.
        /// </remarks>
        internal void ApplyArgumentSetTraits()
        {
            if (_fixtureArguments.Count == 0)
            {
                return;
            }

            var traits = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
#pragma warning disable SA1100 // Do not prefix calls with base unless local implementation exists
            foreach (var kvp in base.Traits)
            {
                traits[kvp.Key] = new HashSet<string>(kvp.Value, StringComparer.OrdinalIgnoreCase);
            }
#pragma warning restore SA1100

            for (int i = 0; i < _fixtureArguments.Count; i++)
            {
                var enumValue = _fixtureArguments[i].EnumValue;
                var enumValueText = enumValue.ToString();

                string key = enumValue.GetType().Name;
                if (!traits.TryGetValue(key, out var values))
                {
                    values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    traits[key] = values;
                }

                values.Add(enumValueText);
            }

            var typedTraits = traits.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyCollection<string>)kvp.Value, StringComparer.OrdinalIgnoreCase);
            TraitsField.SetValue(this, new Lazy<IReadOnlyDictionary<string, IReadOnlyCollection<string>>>(() => typedTraits));
        }
    }
}
