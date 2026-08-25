// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// Encodes and decodes the per-variant <see cref="SingleFlag"/> values as a string array so that they survive the
    /// cross-process transport used by out-of-process (serialized) test-case discovery.
    /// </summary>
    /// <remarks>
    /// The base <see cref="Xunit.v3.XunitTestClass"/>/<see cref="Xunit.v3.XunitTestMethod"/> serialization does not know
    /// about the added <c>Flags</c> property, so the flags are round-tripped explicitly by the <c>IXunitSerializable</c>
    /// implementations on the variant types. Each flag is encoded as <c>"AssemblyQualifiedEnumType=ValueName"</c>.
    /// </remarks>
    internal static class FlagCodec
    {
        /// <summary>
        /// Encodes the supplied flags as an array of <c>"AssemblyQualifiedEnumType=ValueName"</c> strings.
        /// </summary>
        /// <param name="flags">The flags to encode. A <see langword="null"/> value is treated as empty.</param>
        /// <returns>The encoded flag strings.</returns>
        public static string[] Encode(SingleFlag[] flags) =>
            (flags ?? Array.Empty<SingleFlag>())
            .Select(f => f.EnumValue.GetType().AssemblyQualifiedName + "=" + f.EnumValue.ToString())
            .ToArray();

        /// <summary>
        /// Decodes an array produced by <see cref="Encode(SingleFlag[])"/> back into <see cref="SingleFlag"/> values.
        /// </summary>
        /// <param name="encoded">The encoded flag strings. A <see langword="null"/> value is treated as empty.</param>
        /// <returns>The decoded flags.</returns>
        public static SingleFlag[] Decode(string[] encoded)
        {
            if (encoded == null)
            {
                return Array.Empty<SingleFlag>();
            }

            var result = new SingleFlag[encoded.Length];
            for (int i = 0; i < encoded.Length; i++)
            {
                int split = encoded[i].LastIndexOf('=');
#pragma warning disable CA1846 // Type.GetType has no span-based overload, so Substring is required here.
                Type type = Type.GetType(encoded[i].Substring(0, split), throwOnError: true);
                var value = (Enum)Enum.Parse(type, encoded[i].Substring(split + 1));
#pragma warning restore CA1846
                result[i] = new SingleFlag(value);
            }

            return result;
        }
    }
}
