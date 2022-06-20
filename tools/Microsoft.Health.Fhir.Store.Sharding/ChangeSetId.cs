// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Store.Sharding
{
    public struct ChangeSetId : IEquatable<ChangeSetId>, IComparable<ChangeSetId>
    {
        public ChangeSetId(long id)
            : this()
        {
            Id = id;
        }

        public long Id { get; private set; }

        public static bool operator ==(ChangeSetId a, ChangeSetId b)
        {
            return a.Id == b.Id;
        }

        public static bool operator !=(ChangeSetId a, ChangeSetId b)
        {
            return a.Id != b.Id;
        }

        public static bool operator >(ChangeSetId a, ChangeSetId b)
        {
            return a.Id > b.Id;
        }

        public static bool operator >=(ChangeSetId a, ChangeSetId b)
        {
            return a.Id >= b.Id;
        }

        public static bool operator <(ChangeSetId a, ChangeSetId b)
        {
            return a.Id < b.Id;
        }

        public static bool operator <=(ChangeSetId a, ChangeSetId b)
        {
            return a.Id <= b.Id;
        }

        public bool Equals(ChangeSetId other)
        {
            return Id == other.Id;
        }

        public override bool Equals(object obj)
        {
            return obj is ChangeSetId && Equals((ChangeSetId)obj);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        public int CompareTo(ChangeSetId other)
        {
            return Id.CompareTo(other.Id);
        }

        public override string ToString()
        {
            return $"[{Id}]";
        }
    }
}
