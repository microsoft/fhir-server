// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Conformance
{
    public interface IInstantiateCapability
    {
        Task<ICollection<string>> GetCanonicalUrlsAsync(CancellationToken cancellationToken);
    }
}
