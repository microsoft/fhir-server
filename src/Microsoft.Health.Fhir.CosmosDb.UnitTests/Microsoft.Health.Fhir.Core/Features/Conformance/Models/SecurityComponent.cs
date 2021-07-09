// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    public class SecurityComponent
    {
        public SecurityComponent()
        {
            Extension = new List<JObject>();
            Service = new List<CodableConceptInfo>();
        }

        public ICollection<JObject> Extension { get; }

        public ICollection<CodableConceptInfo> Service { get; }
    }
}
