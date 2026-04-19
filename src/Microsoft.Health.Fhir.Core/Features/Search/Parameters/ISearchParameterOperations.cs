// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;

namespace Microsoft.Health.Fhir.Core.Features.Search.Parameters
{
    public interface ISearchParameterOperations
    {
        Task DeleteSearchParameterAsync(RawResource searchParamResource, CancellationToken cancellationToken, bool ignoreSearchParameterNotSupportedException = false);

        Task ValidateSearchParameterAsync(ITypedElement searchParam, CancellationToken cancellationToken);

        Task UpdateSearchParameterStatusAsync(IReadOnlyCollection<string> searchParameterUris, SearchParameterStatus status, CancellationToken cancellationToken, bool ignoreSearchParameterNotSupportedException = false);

        Task EnsureNoActiveReindexJobAsync(CancellationToken cancellationToken);
    }
}
