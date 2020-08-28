// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Search.Registry
{
    public interface ISearchParameterRegistry
    {
        Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatuses();

        Task UpdateStatuses(IEnumerable<ResourceSearchParameterStatus> statuses);
    }
}
