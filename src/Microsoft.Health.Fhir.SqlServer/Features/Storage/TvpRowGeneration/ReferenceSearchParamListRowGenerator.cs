// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    ////internal class ReferenceSearchParamListRowGenerator : ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, ReferenceSearchParamListRow>
    ////{
    ////    private readonly ISqlServerFhirModel _model;

    ////    public ReferenceSearchParamListRowGenerator(ISqlServerFhirModel model)
    ////    {
    ////        _model = EnsureArg.IsNotNull(model, nameof(model));
    ////    }

    ////    public IEnumerable<ReferenceSearchParamListRow> GenerateRows(IReadOnlyList<ResourceWrapper> resources)
    ////    {
    ////        var offset = 0;
    ////        foreach (var resource in resources)
    ////        {
    ////            yield return new ReferenceSearchParamListRow(_model.GetResourceTypeId(resource.ResourceTypeName), offset, 0, null, null, string.Empty, null);
    ////            offset++;
    ////        }
    ////    }
    ////}
}
