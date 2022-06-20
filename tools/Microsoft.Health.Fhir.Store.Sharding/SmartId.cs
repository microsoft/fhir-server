// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Store.Sharding
{
    public struct SmartId : IEquatable<SmartId>
    {
        private const int GridIdShift = 64 - 28 - 1; // TODO: Remove
        private const int ShardletIdShift = GridIdShift - 11;
        public const int MaxSequence = (1 << ShardletIdShift) - 1;

        public SmartId(long id)
            : this()
        {
            Id = id;
        }

        public SmartId(ShardletId shardletId, int sequence)
            : this()
        {
            if (shardletId.Id > 0 && (sequence < 0 || sequence > MaxSequence))
            {
                throw new ArgumentOutOfRangeException(string.Format("Sequence must be between 0 and {0}.", MaxSequence));
            }

            Id = ((long)shardletId.Id << ShardletIdShift) + sequence;
        }

        public long Id { get; private set; }

        public static bool operator ==(SmartId a, SmartId b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(SmartId a, SmartId b)
        {
            return !a.Equals(b);
        }

        public ShardletId ParseShardletId()
        {
            long id;
            if (TempGridId() > 0)
            {
                id = Id >> ShardletIdShift;
                id = id & ShardletId.MaxValue;
            }
            else
            {
                id = 0;
            }

            return new ShardletId((short)id);
        }

        public int ParseSequence()
        {
            return (int)(TempGridId() > 0 ? (Id & MaxSequence) : Id);
        }

        public static ShardletId GetHashedShardletId(string value)
        {
            return new SmartId(GetPermanentHashCode(value)).ParseShardletId();
        }

        public static ShardletId GetHashedShardletId(Guid value)
        {
            return new SmartId(GetPermanentHashCode(value)).ParseShardletId();
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

        private static int GetPermanentHashCode(Guid id)
        {
            var bytes = id.ToByteArray();
            var hashCode = 0;
            foreach (var b in bytes) // Don't convert to LINQ. This is 10% faster.
            {
                hashCode = unchecked((hashCode * 251) + b);
            }

            return hashCode;
        }

        private long TempGridId()
        {
            return Id >> GridIdShift;
        }

        public bool Equals(SmartId other)
        {
            return Id == other.Id;
        }

#pragma warning disable CS8765 // Nullability of type of parameter doesn't match overridden member (possibly because of nullability attributes).
        public override bool Equals(object obj)
#pragma warning restore CS8765 // Nullability of type of parameter doesn't match overridden member (possibly because of nullability attributes).
        {
            return obj is SmartId && Equals((SmartId)obj);
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
