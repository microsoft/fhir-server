// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Conformance.Schema;
using Microsoft.Health.Fhir.Core.Features.Conformance.Serialization;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "This is a DTO-style class")]
    public class ListedResourceComponent
    {
        public ListedResourceComponent()
        {
            Interaction = new HashSet<ResourceInteractionComponent>();
            SearchParam = new HashSet<SearchParamComponent>();
            Versioning = new HashSet<string>();
            SearchRevInclude = new HashSet<string>();
            SearchInclude = new HashSet<string>();
            ReferencePolicy = new HashSet<string>();

            ConditionalUpdate = false;
            ConditionalCreate = false;
            ConditionalDelete = new HashSet<string>();
            ConditionalRead = new HashSet<string>();
        }

        [SchemaOptions]
        public bool? UpdateCreate { get; set; }

        [SchemaOptions]
        public bool? ConditionalUpdate { get; set; }

        [SchemaOptions]
        public bool? ConditionalCreate { get; set; }

        [SchemaOptions]
        public bool? ReadHistory { get; set; }

        [SchemaConst]
        public string Type { get; set; }

        public string Profile { get; set; }

        [SchemaOptions]
        public HashSet<ResourceInteractionComponent> Interaction { get; set; }

        [SchemaOptions]
        public HashSet<SearchParamComponent> SearchParam { get; set; }

        [SchemaOptions]
        public HashSet<string> ConditionalDelete { get; set; }

        [SchemaOptions]
        public HashSet<string> ConditionalRead { get; set; }

        [SelectSingle("versioned")]
        public HashSet<string> Versioning { get; set; }

        [SchemaOptions]
        public HashSet<string> ReferencePolicy { get; set; }

        [SchemaOptions]
        public HashSet<string> SearchRevInclude { get; set; }

        [SchemaOptions]
        public HashSet<string> SearchInclude { get; set; }
    }
}
