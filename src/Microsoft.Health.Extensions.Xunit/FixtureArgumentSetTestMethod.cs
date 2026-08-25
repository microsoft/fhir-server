// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit.v3;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// A <see cref="XunitTestMethod"/> whose traits are the attribute-derived traits merged with the variant's
    /// <c>DataStore</c>/<c>Format</c> values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reflection point #5. The variant traits must be <em>added</em> to the already-merged attribute traits, not replace
    /// them, so that CI trait filters such as <c>Category=...</c> keep matching. <see cref="XunitTestMethod"/> stores the
    /// aggregated assembly/class/method traits in a private field, which is copied and extended here.
    /// </para>
    /// <para>
    /// Pinned to xunit.v3 3.2.2. The backing field is a <see cref="Lazy{T}"/> of
    /// <see cref="IReadOnlyDictionary{TKey, TValue}"/> (not a plain dictionary); assigning the wrong type throws at
    /// discovery, which xUnit v3 silently swallows, so it fails loudly at type load if the field is gone.
    /// </para>
    /// </remarks>
    internal sealed class FixtureArgumentSetTestMethod : XunitTestMethod
    {
        private static readonly FieldInfo TraitsField =
            typeof(XunitTestMethod).GetField("traits", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("XunitTestMethod.traits (private) was not found. The xunit.v3 3.2.2 pin has changed; per-variant trait merge cannot be applied.");

        public FixtureArgumentSetTestMethod(IXunitTestClass testClass, MethodInfo method, SingleFlag[] flags, string uniqueId)
            : base(testClass, method, Array.Empty<object>(), uniqueId)
        {
            Flags = flags;
        }

        /// <summary>
        /// Gets the single-bit flag values (one per dimension) that identify this variant.
        /// </summary>
        public SingleFlag[] Flags { get; }

        /// <summary>
        /// Merges the variant's flag values into the attribute-derived traits.
        /// </summary>
        public void ApplyArgumentSetTraits()
        {
            var merged = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, IReadOnlyCollection<string>> trait in Traits)
            {
                merged[trait.Key] = new List<string>(trait.Value);
            }

            foreach (SingleFlag flag in Flags)
            {
                string key = flag.EnumValue.GetType().Name;
                var values = merged.TryGetValue(key, out IReadOnlyCollection<string> existing) ? new List<string>(existing) : new List<string>();
                values.Add(flag.EnumValue.ToString());
                merged[key] = values;
            }

            IReadOnlyDictionary<string, IReadOnlyCollection<string>> snapshot = merged;
            TraitsField.SetValue(this, new Lazy<IReadOnlyDictionary<string, IReadOnlyCollection<string>>>(() => snapshot));
        }
    }
}
