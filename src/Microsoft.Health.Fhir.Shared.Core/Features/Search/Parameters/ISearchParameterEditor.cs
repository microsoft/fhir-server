// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters
{
    public interface ISearchParameterEditor
    {
        System.Threading.Tasks.Task AddSearchParameterAsync(SearchParameter searchParam, CancellationToken cancellationToken);
    }
}
