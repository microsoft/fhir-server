// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public interface ITerminologyServiceProxy
    {
        Task<ResourceElement> ExpandAsync(
            IReadOnlyList<Tuple<string, string>> parameters,
            string resourceId,
            CancellationToken cancellationToken);
    }
}
