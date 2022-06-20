// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Store.Shards
{
    public struct ShardId : IEquatable<ShardId>
    {
        public ShardId(byte id)
            : this()
        {
            Id = id;
        }

        public byte Id { get; private set; }

        public static bool operator ==(ShardId a, ShardId b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(ShardId a, ShardId b)
        {
            return !a.Equals(b);
        }

        public bool Equals(ShardId other)
        {
            return Id == other.Id;
        }

#pragma warning disable CS8765 // Nullability of type of parameter doesn't match overridden member (possibly because of nullability attributes).
        public override bool Equals(object obj)
#pragma warning restore CS8765 // Nullability of type of parameter doesn't match overridden member (possibly because of nullability attributes).
        {
            return obj is ShardId && Equals((ShardId)obj);
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
