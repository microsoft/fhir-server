// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    public class ListedCapabilityStatement
    {
        internal const string ServerMode = "server";

        public ListedCapabilityStatement()
        {
            Status = new DefaultOptionHashSet<string>("draft", StringComparer.Ordinal);
            Kind = new DefaultOptionHashSet<string>("capability", StringComparer.Ordinal);
            Rest = new HashSet<ListedRestComponent>(new PropertyEqualityComparer<ListedRestComponent>(x => x.Mode));
            Format = new HashSet<string>(StringComparer.Ordinal);
            AdditionalData = new Dictionary<string, JToken>();
            Profile = new List<ReferenceComponent>();
        }

        public string ResourceType { get; } = "CapabilityStatement";

        public Uri Url { get; set; }

        public string Id { get; set; }

        public string Version { get; set; }

        public string Name { get; set; }

        public ICollection<string> Status { get; }

        public bool Experimental { get; set; }

        public string Publisher { get; set; }

        public ICollection<string> Kind { get; }

        public SoftwareComponent Software { get; set; }

        public string Date { get; set; }

        public string FhirVersion { get; set; }

        public ICollection<string> Format { get; }

        public ICollection<ListedRestComponent> Rest { get; }

        [JsonExtensionData]
        public IDictionary<string, JToken> AdditionalData { get; }

        public ICollection<ReferenceComponent> Profile { get; }
    }
}
