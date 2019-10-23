// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "This is a DTO-style class")]
    public class ListedRestComponent
    {
        public ListedRestComponent()
        {
            Resource = new HashSet<ListedResourceComponent>(new PropertyEqualityComparer<ListedResourceComponent>(x => x.Type));
            Interaction = new HashSet<ResourceInteractionComponent>(new PropertyEqualityComparer<ResourceInteractionComponent>(x => x.Code));
            SearchParam = new HashSet<SearchParamComponent>(new PropertyEqualityComparer<SearchParamComponent>(x => x.Name, x => x.Type.ToString()));
            Operation = new HashSet<OperationComponent>(new PropertyEqualityComparer<OperationComponent>(x => x.Name, x => x.Definition));
        }

        public string Documentation { get; set; }

        public string Mode { get; set; }

        public ICollection<ListedResourceComponent> Resource { get; set; }

        public SecurityComponent Security { get; set; }

        public ICollection<ResourceInteractionComponent> Interaction { get; set; }

        public ICollection<SearchParamComponent> SearchParam { get; set; }

        public ICollection<OperationComponent> Operation { get; set; }
    }
}
