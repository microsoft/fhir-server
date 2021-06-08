// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.MemberMatch
{
    public interface IMemberMatchService
    {
        Task<ResourceElement> FindMatch(ResourceElement coverage, ResourceElement patient, CancellationToken cancellationToken);
    }
}
