// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Store.Sharding
{
    public struct ShardletId : IEquatable<ShardletId>
    {
        public const short MinValue = 0;
        public const short MaxReservedValue = 1023;
        public const short MaxValue = 2047;
        public const short TestMinValue = 1;
        public const short TestMaxValue = 64; // it should be >= number of shards in the environment tested

        public ShardletId(short id)
            : this()
        {
            if (id < MinValue || id > MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(id), string.Format("Value must be between {0} and {1}.", MinValue, MaxValue));
            }

            Id = id;
        }

        public short Id { get; private set; }

        public static bool operator ==(ShardletId a, ShardletId b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(ShardletId a, ShardletId b)
        {
            return !a.Equals(b);
        }

        public bool Equals(ShardletId other)
        {
            return Id == other.Id;
        }

#pragma warning disable CS8765 // Nullability of type of parameter doesn't match overridden member (possibly because of nullability attributes).
        public override bool Equals(object obj)
#pragma warning restore CS8765 // Nullability of type of parameter doesn't match overridden member (possibly because of nullability attributes).
        {
            return obj is ShardletId && Equals((ShardletId)obj);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        public override string ToString()
        {
            return Id.ToString();
        }
    }
}
