// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Models;

public class HistoryModel
{
    [FromQuery(Name = KnownQueryParameterNames.At)]
    public PartialDateTime At { get; set; }

    [FromQuery(Name = KnownQueryParameterNames.Since)]
    public PartialDateTime Since { get; set; }

    [FromQuery(Name = KnownQueryParameterNames.Before)]
    public PartialDateTime Before { get; set; }

    [FromQuery(Name = KnownQueryParameterNames.Count)]
    public int? Count { get; set; }

    [FromQuery(Name = KnownQueryParameterNames.ContinuationToken)]
    public string ContinuationToken { get; set; }

    [FromQuery(Name = KnownQueryParameterNames.Sort)]
    public string Sort { get; set; }
}
