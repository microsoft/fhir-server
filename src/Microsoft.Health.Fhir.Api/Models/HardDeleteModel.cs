// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;
using Microsoft.Health.Fhir.Core.Features;

namespace Microsoft.Health.Fhir.Api.Models
{
    public class HardDeleteModel
    {
        [FromQuery(Name = KnownQueryParameterNames.BulkHardDelete)]
        public bool? BulkHardDelete { get; set; }

        [FromQuery(Name = KnownQueryParameterNames.HardDelete)]
        public bool? HardDelete { get; set; }

        public bool IsHardDelete => (BulkHardDelete ?? false) || (HardDelete ?? false);
    }
}
