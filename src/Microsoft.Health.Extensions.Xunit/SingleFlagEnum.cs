// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.Extensions.Xunit
{
    public struct SingleFlagEnum : IEquatable<SingleFlagEnum>
    {
        public SingleFlagEnum(Enum enumValue)
        {
            EnsureArg.IsNotNull(enumValue, nameof(enumValue));

            if (!EnumHelper.IsPowerOfTwo(Convert.ToInt64(enumValue)))
            {
                throw new ArgumentException("Value must be have a single flag", nameof(enumValue));
            }

            EnumValue = enumValue;
        }

        public Enum EnumValue { get; }

        public static bool operator ==(SingleFlagEnum left, SingleFlagEnum right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(SingleFlagEnum left, SingleFlagEnum right)
        {
            return !(left == right);
        }

        public bool Equals(SingleFlagEnum other)
        {
            return other.EnumValue.Equals(EnumValue);
        }

        public override bool Equals(object obj)
        {
            return obj is SingleFlagEnum sfe && Equals(sfe);
        }

        public override int GetHashCode()
        {
            return EnumValue != null ? EnumValue.GetHashCode() : 0;
        }
    }
}
