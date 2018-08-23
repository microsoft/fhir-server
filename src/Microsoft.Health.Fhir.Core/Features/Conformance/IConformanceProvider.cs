// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public interface IConformanceProvider
    {
        Task<CapabilityStatement> GetCapabilityStatementAsync(CancellationToken cancellationToken = default(CancellationToken));

        Task<bool> SatisfiesAsync(IEnumerable<CapabilityQuery> queries, CancellationToken cancellationToken = default(CancellationToken));
    }
}
