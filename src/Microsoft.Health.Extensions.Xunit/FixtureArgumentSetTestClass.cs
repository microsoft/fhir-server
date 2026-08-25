// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Reflection;
using Xunit.Sdk;
using Xunit.v3;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// A <see cref="XunitTestClass"/> representing one <c>(DataStore, Format)</c> variant of a fixtured test class.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reflection point #2. <see cref="XunitTestClass"/> computes a non-virtual <c>uniqueID</c> from the CLR type name, so
    /// every variant of a single type would otherwise share one identity. When that happens xUnit v3 runs the variants in
    /// a single class run with a single class-fixture instance, so only one <c>(DataStore, Format)</c> is ever seeded and
    /// the remaining variants silently execute against the wrong data store while still passing. To break the collision the
    /// per-variant <c>uniqueID</c> is written through reflection after construction.
    /// </para>
    /// <para>
    /// Pinned to xunit.v3 3.2.2. If the private field is renamed in a future version this fails loudly at type load rather
    /// than silently reusing one fixture across variants.
    /// </para>
    /// <para>
    /// The base <see cref="XunitTestClass"/>.<c>Serialize</c>/<c>Deserialize</c> methods are <c>virtual</c> but sealed
    /// (<c>final</c>) in 3.2.2, so they cannot be overridden. To carry the added <c>Flags</c> across the serialized
    /// (out-of-process) discovery transport this type re-declares <see cref="IXunitSerializable"/> and provides explicit
    /// implementations that chain <c>base.Serialize</c>/<c>base.Deserialize</c> (a sealed method is still callable via
    /// <c>base.</c>) before adding the flags. Do not convert these to <c>override</c> — that will not compile. The
    /// reflected <c>uniqueID</c> survives for free because the base serializes the field that was set.
    /// </para>
    /// </remarks>
    internal sealed class FixtureArgumentSetTestClass : XunitTestClass, IXunitSerializable
    {
        private static readonly FieldInfo UniqueIdField =
            typeof(XunitTestClass).GetField("uniqueID", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("XunitTestClass.uniqueID (private) was not found. The xunit.v3 3.2.2 pin has changed; per-variant class identity cannot be set.");

#pragma warning disable CS0618 // The base parameterless constructor is obsolete but is required by the IXunitSerializable deserializer.
        /// <summary>
        /// Initializes a new instance of the <see cref="FixtureArgumentSetTestClass"/> class. Required by
        /// <see cref="IXunitSerializable"/> (analyzer xUnit3001); the deserializer constructs the instance through this
        /// parameterless constructor and then calls <see cref="IXunitSerializable.Deserialize"/>.
        /// </summary>
        public FixtureArgumentSetTestClass()
        {
        }
#pragma warning restore CS0618

        public FixtureArgumentSetTestClass(Type type, IXunitTestCollection collection, SingleFlag[] flags, string uniqueId)
            : base(type, collection)
        {
            Flags = flags;
            UniqueIdField.SetValue(this, uniqueId);
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
    }
}
