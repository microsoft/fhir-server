// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using static Hl7.Fhir.Model.CapabilityStatement;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public class ListedResourceComponent
    {
        public ListedResourceComponent()
        {
            Interaction = new List<ResourceInteractionComponent>();
            SearchParam = new List<SearchParamComponent>();
            Versioning = new List<ResourceVersionPolicy>();
            SearchRevInclude = new List<string>();
            SearchInclude = new List<string>();
            ReferencePolicy = new List<ReferenceHandlingPolicy?>();

            ConditionalUpdate = false;
            ConditionalCreate = false;
            ConditionalDelete = new[] { ConditionalDeleteStatus.NotSupported };
            ConditionalRead = new[] { ConditionalReadStatus.NotSupported };
        }

        public bool? UpdateCreate { get; set; }

        public bool? ConditionalUpdate { get; set; }

        public bool? ConditionalCreate { get; set; }

        public bool? ReadHistory { get; set; }

        public ResourceType? Type { get; set; }

        public ResourceReference Profile { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "This is a DTO-style class")]
        public IList<ResourceInteractionComponent> Interaction { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "This is a DTO-style class")]
        public IList<SearchParamComponent> SearchParam { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "This is a DTO-style class")]
        public IList<ConditionalDeleteStatus> ConditionalDelete { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "This is a DTO-style class")]
        public IList<ConditionalReadStatus> ConditionalRead { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "This is a DTO-style class")]
        public IList<ResourceVersionPolicy> Versioning { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "This is a DTO-style class")]
        public IList<string> SearchRevInclude { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "This is a DTO-style class")]
        public IList<string> SearchInclude { get; set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "This is a DTO-style class")]
        public IList<ReferenceHandlingPolicy?> ReferencePolicy { get; set; }
    }
}
