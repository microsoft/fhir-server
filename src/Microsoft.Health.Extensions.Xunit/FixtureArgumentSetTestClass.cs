// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Xunit.Sdk;
using Xunit.v3;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// A <see cref="XunitTestClass"/> representing one <c>(DataStore, Format)</c> variant of a fixtured test class.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each variant supplies its own class identity so xUnit creates a separate class-fixture instance for each
    /// <c>(DataStore, Format)</c> combination. Native methods inherit the variant traits through class metadata.
    /// </para>
    /// <para>
    /// The base serialization methods cannot be overridden, so explicit <see cref="IXunitSerializable"/>
    /// implementations add the fixture flags to the native class metadata, which already includes the variant identity.
    /// </para>
    /// </remarks>
    internal sealed class FixtureArgumentSetTestClass : XunitTestClass, IXunitSerializable, ITestClassMetadata
    {
        private readonly Lazy<IReadOnlyDictionary<string, IReadOnlyCollection<string>>> _traits;

#pragma warning disable CS0618 // The base parameterless constructor is obsolete but is required by the IXunitSerializable deserializer.
        /// <summary>
        /// Initializes a new instance of the <see cref="FixtureArgumentSetTestClass"/> class. Required by
        /// <see cref="IXunitSerializable"/> (analyzer xUnit3001); the deserializer constructs the instance through this
        /// parameterless constructor and then calls <see cref="IXunitSerializable.Deserialize"/>.
        /// </summary>
        public FixtureArgumentSetTestClass()
        {
            _traits = new(CreateTraits);
        }
#pragma warning restore CS0618

        public FixtureArgumentSetTestClass(Type type, IXunitTestCollection collection, SingleFlag[] flags, string uniqueId)
            : base(type, collection, uniqueId)
        {
            Flags = flags;
            _traits = new(CreateTraits);
        }

        /// <summary>
        /// Gets the single-bit flag values (one per dimension) that identify this variant.
        /// </summary>
        public SingleFlag[] Flags { get; private set; }

        // Native methods and deferred theory rows inherit traits through the class metadata interface.
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> ITestClassMetadata.Traits => _traits.Value;

        private Dictionary<string, IReadOnlyCollection<string>> CreateTraits()
        {
            var traits = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, IReadOnlyCollection<string>> trait in Traits)
            {
                traits[trait.Key] = new List<string>(trait.Value);
            }

            foreach (SingleFlag flag in Flags)
            {
                string key = flag.EnumValue.GetType().Name;
                var values = traits.TryGetValue(key, out IReadOnlyCollection<string> existing) ? new List<string>(existing) : new List<string>();
                values.Add(flag.EnumValue.ToString());
                traits[key] = values;
            }

            return traits;
        }

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
    }
}
