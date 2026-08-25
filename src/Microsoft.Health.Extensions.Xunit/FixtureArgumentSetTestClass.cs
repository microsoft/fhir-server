// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Reflection;
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
    /// </remarks>
    internal sealed class FixtureArgumentSetTestClass : XunitTestClass
    {
        private static readonly FieldInfo UniqueIdField =
            typeof(XunitTestClass).GetField("uniqueID", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("XunitTestClass.uniqueID (private) was not found. The xunit.v3 3.2.2 pin has changed; per-variant class identity cannot be set.");

        public FixtureArgumentSetTestClass(Type type, IXunitTestCollection collection, SingleFlag[] flags, string uniqueId)
            : base(type, collection)
        {
            Flags = flags;
            UniqueIdField.SetValue(this, uniqueId);
        }

        /// <summary>
        /// Gets the single-bit flag values (one per dimension) that identify this variant.
        /// </summary>
        public SingleFlag[] Flags { get; }
    }
}
