// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkResourceWriteClaimV1RowGenerator : ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkResourceWriteClaimTableTypeV1Row>
    {
        private readonly ISqlServerFhirModel _model;
        private readonly SearchParameterToSearchValueTypeMap _searchParameterTypeMap;

        public BulkResourceWriteClaimV1RowGenerator(ISqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
        {
            EnsureArg.IsNotNull(model, nameof(model));
            EnsureArg.IsNotNull(searchParameterTypeMap, nameof(searchParameterTypeMap));

            _model = model;
            _searchParameterTypeMap = searchParameterTypeMap;
        }

        public IEnumerable<BulkResourceWriteClaimTableTypeV1Row> GenerateRows(IReadOnlyList<ResourceWrapper> resources)
        {
            for (var index = 0; index < resources.Count; index++)
            {
                ResourceWrapper resource = resources[index];

                var resourceMetadata = new ResourceMetadata(
                    resource.CompartmentIndices,
                    resource.SearchIndices?.ToLookup(e => _searchParameterTypeMap.GetSearchValueType(e)),
                    resource.LastModifiedClaims);

                IReadOnlyCollection<KeyValuePair<string, string>> writeClaims = resourceMetadata.WriteClaims;
                if (writeClaims == null)
                {
                    yield break;
                }

                foreach (var claim in writeClaims)
                {
                    yield return new BulkResourceWriteClaimTableTypeV1Row(index, _model.GetClaimTypeId(claim.Key), claim.Value);
                }
            }
        }
    }
}
