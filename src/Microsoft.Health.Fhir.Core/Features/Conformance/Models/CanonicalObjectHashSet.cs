// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "Should be consistent with base type.")]
    internal class CanonicalObjectHashSet<T> : HashSet<T>, ICanonicalObject
    {
        public CanonicalObjectHashSet(T canonicalObject)
        {
            CanonicalObject = canonicalObject;
        }

        public CanonicalObjectHashSet()
        {
        }

        public CanonicalObjectHashSet(T canonicalObject, IEqualityComparer<T> comparer)
            : base(comparer)
        {
            CanonicalObject = canonicalObject;
        }

        public T CanonicalObject { get; set;  }

        object ICanonicalObject.CanonicalObject
        {
            get
            {
                return CanonicalObject;
            }
        }
    }
}
