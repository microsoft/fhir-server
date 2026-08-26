// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit.Sdk;
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
    /// <para>
    /// The base <see cref="XunitTestMethod"/>.<c>Serialize</c>/<c>Deserialize</c> methods are <c>virtual</c> but sealed
    /// (<c>final</c>) in 3.2.2, so they cannot be overridden. To carry the added <c>Flags</c> across the serialized
    /// (out-of-process) discovery transport this type re-declares <see cref="IXunitSerializable"/> and provides explicit
    /// implementations that chain <c>base.Serialize</c>/<c>base.Deserialize</c> (a sealed method is still callable via
    /// <c>base.</c>). Do not convert these to <c>override</c> — that will not compile.
    /// </para>
    /// <para>
    /// Only <c>Flags</c> is carried across serialization; the base does <em>not</em> persist the reflectively-injected
    /// merged traits, so a method whose CLR attributes carry no <c>[Trait]</c> deserializes without the variant's
    /// <c>DataStore</c>/<c>Format</c> traits. This is safe for every path this repo uses: trait filtering is consumed at
    /// discovery time (in-process, before any case is serialized), and on v2 the CI legs filtered on the display name
    /// (<c>FullyQualifiedName!~SqlServer</c>) so these traits did not exist at all — losing them after deserialization
    /// cannot regress against v2. The display name, which Azure DevOps test history keys on, does survive. The one
    /// consumer that would be affected is a post-deserialization reader of variant traits in the execution process — for
    /// example trait-based TRX categorization or grouping. CI does not do this today; anyone adding it must re-apply
    /// <see cref="ApplyArgumentSetTraits"/> on the deserialize side.
    /// </para>
    /// </remarks>
    internal sealed class FixtureArgumentSetTestMethod : XunitTestMethod, IXunitSerializable
    {
        private static readonly FieldInfo TraitsField =
            typeof(XunitTestMethod).GetField("traits", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("XunitTestMethod.traits (private) was not found. The xunit.v3 3.2.2 pin has changed; per-variant trait merge cannot be applied.");

#pragma warning disable CS0618 // The base parameterless constructor is obsolete but is required by the IXunitSerializable deserializer.
        /// <summary>
        /// Initializes a new instance of the <see cref="FixtureArgumentSetTestMethod"/> class. Required by
        /// <see cref="IXunitSerializable"/> (analyzer xUnit3001); the deserializer constructs the instance through this
        /// parameterless constructor and then calls <see cref="IXunitSerializable.Deserialize"/>.
        /// </summary>
        public FixtureArgumentSetTestMethod()
        {
        }
#pragma warning restore CS0618

        public FixtureArgumentSetTestMethod(IXunitTestClass testClass, MethodInfo method, SingleFlag[] flags, string uniqueId)
            : base(testClass, method, Array.Empty<object>(), uniqueId)
        {
            Flags = flags;
        }

        /// <summary>
        /// Gets the single-bit flag values (one per dimension) that identify this variant.
        /// </summary>
        public SingleFlag[] Flags { get; private set; }

#pragma warning disable SA1100 // base. is REQUIRED: the sealed (virtual final) base Serialize/Deserialize cannot be reached via this. from an explicit interface implementation.
        void IXunitSerializable.Serialize(IXunitSerializationInfo info)
        {
            base.Serialize(info);
            info.AddValue("variantFlags", FlagCodec.Encode(Flags));
        }

        void IXunitSerializable.Deserialize(IXunitSerializationInfo info)
        {
            base.Deserialize(info);
            Flags = FlagCodec.Decode(info.GetValue<string[]>("variantFlags"));
        }
#pragma warning restore SA1100

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
