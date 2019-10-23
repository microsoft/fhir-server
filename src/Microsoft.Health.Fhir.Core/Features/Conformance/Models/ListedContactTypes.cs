// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "This is a DTO-style class")]
    public class ListedContactTypes
    {
        public ListedContactTypes()
        {
            Telecom = new HashSet<ListedContactPoint>(
                new PropertyEqualityComparer<ListedContactPoint>(x => x.System, x => x.Use, x => x.Value));
        }

        public ICollection<ListedContactPoint> Telecom { get; protected set; }
    }
}
