// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Models;

/// <summary>
/// Query parameters for searching soft-deleted resources.
/// </summary>
public class DeletedResourceSearchModel
{
    /// <summary>
    /// Gets or sets the inclusive lower last-updated bound.
    /// </summary>
    [FromQuery(Name = KnownQueryParameterNames.Since)]
    public PartialDateTime Since { get; set; }

    /// <summary>
    /// Gets or sets the exclusive upper last-updated bound.
    /// </summary>
    [FromQuery(Name = KnownQueryParameterNames.Before)]
    public PartialDateTime Before { get; set; }

    /// <summary>
    /// Gets or sets the page size.
    /// </summary>
    [FromQuery(Name = KnownQueryParameterNames.Count)]
    public int? Count { get; set; }

    /// <summary>
    /// Gets or sets the continuation token.
    /// </summary>
    [FromQuery(Name = KnownQueryParameterNames.ContinuationToken)]
    public string ContinuationToken { get; set; }

    /// <summary>
    /// Gets or sets the last-updated sort order.
    /// </summary>
    [FromQuery(Name = KnownQueryParameterNames.Sort)]
    public string Sort { get; set; }
}
