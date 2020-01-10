// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix", Justification = "Should be consistent with base type.")]
    internal class ReferenceObjectHashSet<T> : HashSet<T>, IReferenceObject
    {
        public ReferenceObjectHashSet(T referenceObject)
        {
            ReferenceObject = referenceObject;
        }

        public ReferenceObjectHashSet()
        {
        }

        public ReferenceObjectHashSet(T referenceObject, IEqualityComparer<T> comparer)
            : base(comparer)
        {
            ReferenceObject = referenceObject;
        }

        public T ReferenceObject { get; set;  }

        object IReferenceObject.ReferenceObject
        {
            get
            {
                return ReferenceObject;
            }
        }
    }
}
