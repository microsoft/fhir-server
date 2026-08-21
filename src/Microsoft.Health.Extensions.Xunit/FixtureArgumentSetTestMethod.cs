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
        private static readonly FieldInfo TestMethodArgumentsField = GetRequiredField("testMethodArguments");
        private static readonly FieldInfo TraitsField = GetRequiredField("traits");
        private static readonly FieldInfo UniqueIdField = GetRequiredField("uniqueID");
        private static readonly FieldInfo MethodField = GetRequiredField("method");

        private readonly FixtureArgumentSetTestClass _testClass;
        private readonly MethodInfo _methodInfo;
        private readonly IReadOnlyList<SingleFlag> _fixtureArguments;
        private readonly string _uniqueId;

        public FixtureArgumentSetTestMethod(FixtureArgumentSetTestClass testClass, MethodInfo methodInfo, IReadOnlyList<SingleFlag> fixtureArguments, string uniqueId)
            : base(testClass, methodInfo, testMethodArguments: Array.Empty<object>(), uniqueId)
        {
            EnsureArg.IsNotNull(testClass, nameof(testClass));
            EnsureArg.IsNotNull(methodInfo, nameof(methodInfo));
            EnsureArg.IsNotNull(fixtureArguments, nameof(fixtureArguments));

            _testClass = testClass;
            _methodInfo = methodInfo;
            _fixtureArguments = fixtureArguments;
            _uniqueId = uniqueId;

            UniqueIdField.SetValue(this, _uniqueId);
            MethodField.SetValue(this, _methodInfo);
        }

#pragma warning disable CS0618 // Called by the de-serializer; should only be called by deriving classes for de-serialization purposes
        public FixtureArgumentSetTestMethod()
        {
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

        private object[] CombineFixtureAndMethodArguments(object[] methodArguments)
        {
            if (_fixtureArguments.Count == 0)
            {
                return methodArguments;
            }

            var result = new object[_fixtureArguments.Count + methodArguments.Length];
            for (int i = 0; i < _fixtureArguments.Count; i++)
            {
                result[i] = _fixtureArguments[i].EnumValue;
            }

            if (methodArguments.Length > 0)
            {
                Array.Copy(methodArguments, 0, result, _fixtureArguments.Count, methodArguments.Length);
            }

            return result;
        }

        private void UpdateMethodArguments(object[] methodArguments)
        {
            if (_fixtureArguments.Count == 0)
            {
                return;
            }

            TestMethodArgumentsField.SetValue(this, methodArguments);
        }

        internal void UpdateArgumentsFromMethod()
        {
            if (_fixtureArguments.Count == 0)
            {
                return;
            }

            var combinedArguments = CombineFixtureAndMethodArguments(Array.Empty<object>());
            TestMethodArgumentsField.SetValue(this, combinedArguments);

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
