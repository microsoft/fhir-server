// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using static Hl7.Fhir.Model.CapabilityStatement;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "This is a DTO-style class")]
    public sealed class ListedCapabilityStatement
    {
        public Uri Url { get; set; }

        public string Id { get; set; }

        public string Version { get; set; }

        public string Name { get; set; }

        public IList<PublicationStatus> Status { get; set; }

        public bool Experimental { get; set; }

        public string Publisher { get; set; }

        public IList<ListedContactPoint> Telecom { get; set; }

        public IList<CapabilityStatementKind> Kind { get; set; }

        public SoftwareComponent Software { get; set; }

        public string FhirVersion { get; set; }

        public IList<UnknownContentCode> AcceptUnknown { get; set; }

        public IList<string> Format { get; set; }

        public IList<ListedRestComponent> Rest { get; set; }

        public IList<Code<PublicationStatus>> StatusElement { get; set; }
    }
}
