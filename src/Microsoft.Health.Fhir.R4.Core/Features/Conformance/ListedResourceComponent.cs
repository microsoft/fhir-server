// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using static Hl7.Fhir.Model.CapabilityStatement;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "This is a DTO-style class")]
    public class ListedResourceComponent : IListedResourceComponent
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

        public string Profile { get; set; }

        public IList<ResourceInteractionComponent> Interaction { get; set; }

        public IList<SearchParamComponent> SearchParam { get; set; }

        public IList<ConditionalDeleteStatus> ConditionalDelete { get; set; }

        public IList<ConditionalReadStatus> ConditionalRead { get; set; }

        public IList<ResourceVersionPolicy> Versioning { get; set; }

        public IList<ReferenceHandlingPolicy?> ReferencePolicy { get; set; }

        public IList<string> SearchRevInclude { get; set; }

        public IList<string> SearchInclude { get; set; }

        public void AddResourceVersionPolicy(string policy)
        {
            EnsureArg.IsNotEmptyOrWhitespace(policy, nameof(policy));

            var versionPolicy = policy.GetValueByEnumLiteral<ResourceVersionPolicy>();

            Versioning.Add(versionPolicy);
        }
    }
}
