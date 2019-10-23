// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "This is a DTO-style class")]
    public class ListedResourceComponent
    {
        public ListedResourceComponent()
        {
            Interaction = new HashSet<ResourceInteractionComponent>(new PropertyEqualityComparer<ResourceInteractionComponent>(x => x.Code));
            SearchParam = new HashSet<SearchParamComponent>(new PropertyEqualityComparer<SearchParamComponent>(x => x.Name, x => x.Type.ToString()));
            Versioning = new DefaultOptionHashSet<string>("versioned");
            SearchRevInclude = new HashSet<string>();
            SearchInclude = new HashSet<string>();
            ReferencePolicy = new HashSet<string>();

            ConditionalUpdate = false;
            ConditionalCreate = false;
            ConditionalDelete = new HashSet<string>();
            ConditionalRead = new HashSet<string>();
        }

        public bool? UpdateCreate { get; set; }

        public bool? ConditionalUpdate { get; set; }

        public bool? ConditionalCreate { get; set; }

        public bool? ReadHistory { get; set; }

        public string Type { get; set; }

        public string Profile { get; set; }

        public ICollection<ResourceInteractionComponent> Interaction { get; set; }

        public ICollection<SearchParamComponent> SearchParam { get; set; }

        public ICollection<string> ConditionalDelete { get; set; }

        public ICollection<string> ConditionalRead { get; set; }

        public ICollection<string> Versioning { get; set; }

        public ICollection<string> ReferencePolicy { get; set; }

        public ICollection<string> SearchRevInclude { get; set; }

        public ICollection<string> SearchInclude { get; set; }
    }
}
