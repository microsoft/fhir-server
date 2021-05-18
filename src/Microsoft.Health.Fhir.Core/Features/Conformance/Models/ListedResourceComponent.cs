// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    public class ListedResourceComponent
    {
        public ListedResourceComponent()
        {
            Interaction = new HashSet<ResourceInteractionComponent>(new PropertyEqualityComparer<ResourceInteractionComponent>(x => x.Code));
            SearchParam = new HashSet<SearchParamComponent>(new PropertyEqualityComparer<SearchParamComponent>(x => x.Name, x => x.Type.ToString()));
            Versioning = new DefaultOptionHashSet<string>("versioned", StringComparer.Ordinal);
            SearchRevInclude = new HashSet<string>(StringComparer.Ordinal);
            SearchInclude = new HashSet<string>(StringComparer.Ordinal);
            ReferencePolicy = new HashSet<string>(StringComparer.Ordinal);
            SupportedProfile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Operation = new HashSet<OperationComponent>(new PropertyEqualityComparer<OperationComponent>(x => x.Name, x => x.Definition.ToString()));

            ConditionalUpdate = false;
            ConditionalCreate = false;
            ConditionalDelete = new DefaultOptionHashSet<string>(ConditionalDeleteStatus.Single, StringComparer.Ordinal);
            ConditionalRead = new HashSet<string>(StringComparer.Ordinal);
        }

        public bool? UpdateCreate { get; set; }

        public bool? ConditionalUpdate { get; set; }

        public bool? ConditionalCreate { get; set; }

        public bool? ReadHistory { get; set; }

        public string Type { get; set; }

        public ReferenceComponent Profile { get; set; }

        public ICollection<string> SupportedProfile { get; }

        public ICollection<ResourceInteractionComponent> Interaction { get; }

        public ICollection<SearchParamComponent> SearchParam { get; }

        public ICollection<string> ConditionalDelete { get; }

        public ICollection<string> ConditionalRead { get; }

        public ICollection<string> Versioning { get; }

        public ICollection<string> ReferencePolicy { get; }

        public ICollection<string> SearchRevInclude { get; }

        public ICollection<string> SearchInclude { get; }

        public ICollection<OperationComponent> Operation { get; }
    }
}
