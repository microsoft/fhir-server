// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Search.Registry
{
    public interface ISearchParameterStatusDataStore
    {
        Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatuses();

        Task<ResourceSearchParameterStatus> GetSearchParameterStatus(Uri searchParameterUri, CancellationToken cancellationToken);

        Task UpsertStatuses(List<ResourceSearchParameterStatus> statuses);
    }
}
