// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Store.Sharding
{
    public struct ShardletId : IEquatable<ShardletId>
    {
        public const byte MinValue = byte.MinValue;
        public const byte MaxValue = byte.MaxValue; // TODO: consider removng generic check logic

        public ShardletId(byte id)
            : this()
        {
            if (id < MinValue || id > MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(id), string.Format("Value must be between {0} and {1}.", MinValue, MaxValue));
            }

            Id = id;
        }

        public byte Id { get; private set; }

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

        public static ShardletId GetHashedShardletId(string value)
        {
            if (value == null)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Value must be not null.");
            }

            return new ShardletId((byte)(GetPermanentHashCode(value) & MaxValue));
        }

        private static int GetPermanentHashCode(string str)
        {
            var hashCode = 0;
            foreach (var c in str) // Don't convert to LINQ. This is 10% faster.
            {
                hashCode = unchecked((hashCode * 251) + c);
            }

            return hashCode;
        }
    }
}
