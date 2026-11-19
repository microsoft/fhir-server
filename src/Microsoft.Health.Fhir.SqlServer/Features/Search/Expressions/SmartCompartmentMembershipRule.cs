// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Immutable;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions
{
    /// <summary>
    /// Describes the materialized reference search parameters that establish compartment membership for a resource type.
    /// </summary>
    internal sealed record SmartCompartmentMembershipRule(
        string ResourceType,
        ImmutableArray<Uri> SearchParameterUrls);
}
