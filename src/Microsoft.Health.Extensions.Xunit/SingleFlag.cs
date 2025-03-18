﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// A struct encapsulating an enum value where the numeric value only has a single bit set.
    /// </summary>
    internal struct SingleFlag : IEquatable<SingleFlag>
    {
        public SingleFlag(Enum enumValue)
        {
            EnsureArg.IsNotNull(enumValue, nameof(enumValue));

            if (!IsPowerOfTwo(Convert.ToInt64(enumValue)))
            {
                throw new ArgumentException("Value must be have a single bit set", nameof(enumValue));
            }

            EnumValue = enumValue;
        }

        public Enum EnumValue { get; }

        public static bool operator ==(SingleFlag left, SingleFlag right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(SingleFlag left, SingleFlag right)
        {
            return !(left == right);
        }

        public bool Equals(SingleFlag other)
        {
            return other.EnumValue.Equals(EnumValue);
        }

        public override bool Equals(object obj)
        {
            return obj is SingleFlag sfe && Equals(sfe);
        }

        public override int GetHashCode()
        {
            return EnumValue != null ? EnumValue.GetHashCode() : 0;
        }

        private static bool IsPowerOfTwo(long x)
        {
            return (x != 0) && ((x & (x - 1)) == 0);
        }
    }
}
