// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;

namespace Microsoft.Health.Fhir.Core.Features.Search.Parameters
{
    public interface ISearchParameterUtilities
    {
        Task AddSearchParameterAsync(ITypedElement searchParam);

        Task DeleteSearchParameterAsync(ITypedElement searchParam);
    }
}
