// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Conformance.Schema;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "This is a DTO-style class")]
    public class ListedRestComponent
    {
        public ListedRestComponent()
        {
            Resource = new HashSet<ListedResourceComponent>(new PropertyEqualityComparer<ListedResourceComponent>(x => x.Type));
            Interaction = new HashSet<ResourceInteractionComponent>();
            SearchParam = new HashSet<SearchParamComponent>();
            Operation = new HashSet<OperationComponent>();
        }

        public string Documentation { get; set; }

        [SchemaConst]
        public string Mode { get; set; }

        [SchemaOptions]
        public HashSet<ListedResourceComponent> Resource { get; set; }

        public SecurityComponent Security { get; set; }

        [SchemaOptions]
        public HashSet<ResourceInteractionComponent> Interaction { get; set; }

        [SchemaOptions]
        public HashSet<SearchParamComponent> SearchParam { get; set; }

        [SchemaOptions]
        public HashSet<OperationComponent> Operation { get; set; }
    }
}
