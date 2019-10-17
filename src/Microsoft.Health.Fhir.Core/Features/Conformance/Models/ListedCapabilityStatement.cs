// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Conformance.Schema;
using Microsoft.Health.Fhir.Core.Features.Conformance.Serialization;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "This is a DTO-style class")]
    public class ListedCapabilityStatement
    {
        internal const string ServerMode = "server";
        internal const string CapabilityStatement = "CapabilityStatement";

        public ListedCapabilityStatement()
        {
            Status = new HashSet<string>();
            Contact = new HashSet<ListedContactTypes>();
            Kind = new HashSet<string>();
            Rest = new HashSet<ListedRestComponent>(new PropertyEqualityComparer<ListedRestComponent>(x => x.Mode));
            Format = new HashSet<string>();
        }

        [SchemaConst]
        public string ResourceType { get; } = CapabilityStatement;

        public Uri Url { get; set; }

        public string Id { get; set; }

        public string Version { get; set; }

        public string Name { get; set; }

        [SelectSingle("draft")]
        public ICollection<string> Status { get; protected set; }

        public bool Experimental { get; set; }

        public string Publisher { get; set; }

        public ICollection<ListedContactTypes> Contact { get; set; }

        [SelectSingle("capability")]
        public ICollection<string> Kind { get; protected set; }

        public SoftwareComponent Software { get; set; }

        [SchemaConst]
        public string FhirVersion { get; set; }

        [SchemaOptions]
        public ICollection<string> Format { get; protected set; }

        [SchemaOptions]
        public ICollection<ListedRestComponent> Rest { get; protected set; }
    }
}
