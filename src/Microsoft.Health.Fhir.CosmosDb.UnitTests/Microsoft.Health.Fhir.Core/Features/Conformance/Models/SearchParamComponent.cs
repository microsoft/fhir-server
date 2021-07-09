// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    public class SearchParamComponent
    {
        public string Name { get; set; }

        public SearchParamType Type { get; set; }

        public Uri Definition { get; set; }

        public string Documentation { get; set; }
    }
}
