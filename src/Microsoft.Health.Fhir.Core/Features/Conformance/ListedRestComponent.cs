// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using static Hl7.Fhir.Model.CapabilityStatement;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "This is a DTO-style class")]
    public class ListedRestComponent
    {
        public string Documentation { get; set; }

        public IList<RestfulCapabilityMode> Mode { get; set; }

        public IList<ListedResourceComponent> Resource { get; set; }

        public SecurityComponent Security { get; set; }

        public List<SystemInteractionComponent> Interaction { get; set; }

        public IList<SearchParamComponent> SearchParam { get; set; }

        public IList<OperationComponent> Operation { get; set; }
    }
}
