// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.Stats
{
    /// <summary>
    /// Request for resource statistics.
    /// </summary>
    public class StatsRequest : IRequest<StatsResponse>
    {
    }
}
