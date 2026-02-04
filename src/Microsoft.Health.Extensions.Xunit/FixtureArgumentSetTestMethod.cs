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
        private static readonly FieldInfo TestMethodArgumentsField = typeof(XunitTestMethod).GetField("testMethodArguments", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo TraitsField = typeof(XunitTestMethod).GetField("traits", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo UniqueIdField = typeof(XunitTestMethod).GetField("uniqueID", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo MethodField = typeof(XunitTestMethod).GetField("method", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly FixtureArgumentSetTestClass _testClass;
        private readonly MethodInfo _methodInfo;
        private readonly IReadOnlyList<SingleFlag> _fixtureArguments;
        private readonly string _uniqueId;

        public FixtureArgumentSetTestMethod(FixtureArgumentSetTestClass testClass, MethodInfo methodInfo, IReadOnlyList<SingleFlag> fixtureArguments, string uniqueId)
            : base(testClass, methodInfo, testMethodArguments: null, uniqueId)
        {
            EnsureArg.IsNotNull(testClass, nameof(testClass));
            EnsureArg.IsNotNull(methodInfo, nameof(methodInfo));
            EnsureArg.IsNotNull(fixtureArguments, nameof(fixtureArguments));

            _testClass = testClass;
            _methodInfo = methodInfo;
            _fixtureArguments = fixtureArguments;
            _uniqueId = uniqueId;

            UpdateMethodArguments(Array.Empty<object>());
            UniqueIdField?.SetValue(this, _uniqueId);
            MethodField?.SetValue(this, _methodInfo);
        }

#pragma warning disable CS0618 // Called by the de-serializer; should only be called by deriving classes for de-serialization purposes
        public FixtureArgumentSetTestMethod()
        {
        }
#pragma warning restore CS0618

#pragma warning disable SA1100 // Do not prefix calls with base unless local implementation exists
        public new string GetDisplayName(string baseDisplayName, string displayName, object[] testMethodArguments, Type[] genericTypes)
        {
            var combinedArguments = CombineFixtureAndMethodArguments(testMethodArguments ?? Array.Empty<object>());
            return base.GetDisplayName(baseDisplayName, displayName, combinedArguments, genericTypes);
        }

        public new object[] ResolveMethodArguments(object[] arguments)
        {
            var combinedArguments = CombineFixtureAndMethodArguments(arguments ?? Array.Empty<object>());
            UpdateMethodArguments(combinedArguments);
            return base.ResolveMethodArguments(combinedArguments);
        }
#pragma warning restore SA1100

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

            TestMethodArgumentsField?.SetValue(this, methodArguments);
        }

        internal void UpdateArgumentsFromMethod()
        {
            if (_fixtureArguments.Count == 0)
            {
                return;
            }

            var combinedArguments = CombineFixtureAndMethodArguments(Array.Empty<object>());
            TestMethodArgumentsField?.SetValue(this, combinedArguments);

            if (TraitsField != null)
            {
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

                    string key = $"FixtureArg{i + 1}";
                    if (!traits.TryGetValue(key, out var values))
                    {
                        values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        traits[key] = values;
                    }

                    values.Add(enumValueText);

                    if (TryGetFixtureTraitName(enumValue, out var traitName))
                    {
                        AddTrait(traits, traitName, enumValueText);
                    }
                }

                var typedTraits = traits.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyCollection<string>)kvp.Value, StringComparer.OrdinalIgnoreCase);
                TraitsField.SetValue(this, new Lazy<IReadOnlyDictionary<string, IReadOnlyCollection<string>>>(() => typedTraits));
            }
        }

        private static void AddTrait(Dictionary<string, HashSet<string>> traits, string key, string value)
        {
            if (!traits.TryGetValue(key, out var values))
            {
                values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                traits[key] = values;
            }

            values.Add(value);
        }

        private static bool TryGetFixtureTraitName(Enum enumValue, out string traitName)
        {
            traitName = null;
            if (enumValue == null)
            {
                return false;
            }

            var enumType = enumValue.GetType();
            if (!string.Equals(enumType.Namespace, "Microsoft.Health.Fhir.Tests.Common.FixtureParameters", StringComparison.Ordinal))
            {
                return false;
            }

            if (string.Equals(enumType.Name, "DataStore", StringComparison.Ordinal))
            {
                traitName = "DataStore";
                return true;
            }

            if (string.Equals(enumType.Name, "Format", StringComparison.Ordinal))
            {
                traitName = "Format";
                return true;
            }

            return false;
        }
    }
}
