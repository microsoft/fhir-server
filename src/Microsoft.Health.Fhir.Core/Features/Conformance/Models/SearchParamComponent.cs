// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Conformance.Schema;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    public class SearchParamComponent
    {
        [SchemaConst]
        public string Name { get; set; }

        [SchemaConst]
        public SearchParamType Type { get; set; }
    }
}
