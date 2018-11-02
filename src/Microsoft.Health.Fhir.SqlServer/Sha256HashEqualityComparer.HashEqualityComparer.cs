// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Microsoft.Health.Fhir.SqlServer
{
    internal class Sha256HashEqualityComparer : IEqualityComparer<(byte[] hash, string text)>
    {
        public static readonly Sha256HashEqualityComparer Instance = new Sha256HashEqualityComparer();

        public bool Equals((byte[] hash, string text) x, (byte[] hash, string text) y) =>
            x.hash.AsSpan().SequenceEqual(y.hash.AsSpan());

        public int GetHashCode((byte[] hash, string text) obj)
        {
            int hashCode = 0;
            foreach (var i in MemoryMarshal.Cast<byte, int>(obj.hash))
            {
                hashCode = CombineHashCodes(hashCode, i);
            }

            return hashCode;
        }

        private static int CombineHashCodes(int left, int right) => ((left << 5) + left) ^ right;
    }
}
