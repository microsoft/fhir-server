// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model.Primitives;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "This is a DTO-style class")]
    public class SecurityComponent
    {
        public SecurityComponent()
        {
            Service = new List<Coding>();
        }

        public JObject Extension { get; set; }

        public IList<Coding> Service { get; set; }
    }
}
