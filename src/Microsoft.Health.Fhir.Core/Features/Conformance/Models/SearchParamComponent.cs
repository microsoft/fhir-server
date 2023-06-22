// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    public class SearchParamComponent : IExtendable
    {
        public string Name { get; set; }

        public SearchParamType Type { get; set; }

        public Uri Definition { get; set; }

        public string Documentation { get; set; }

        // FHIR Extension that contains url that equals "status" and a value that is a FhirString
#pragma warning disable CA2227 // Collection properties should be read only
        public List<Extension> Extension { get; set; }
#pragma warning restore CA2227 // Collection properties should be read only
    }
}
