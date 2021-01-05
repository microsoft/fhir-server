// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters
{
    public interface ISearchParameterValidator
    {
        System.Threading.Tasks.Task ValidateSearchParamterInput(SearchParameter searchParam, string method, CancellationToken cancellationToken);
    }
}
