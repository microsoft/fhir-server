// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search;

[FhirType("ResponseComponent")]
public class RawBundleResponseComponent : Bundle.ResponseComponent
{
    public RawBundleResponseComponent(RawResourceElement rawOutcomeResourceElement)
    {
        EnsureArg.IsNotNull(rawOutcomeResourceElement, nameof(rawOutcomeResourceElement));

        OutcomeElement = rawOutcomeResourceElement;
    }

    /// <summary>
    /// A <see cref="RawResponseComponent"/> with no payload.
    /// </summary>
    public RawBundleResponseComponent()
    {
    }

    public RawResourceElement OutcomeElement { get; set; }
}
