// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import
{
    public class SqlBulkCopyDataWrapperFactory
    {
        private SqlServerFhirModel _model;
        private SearchParameterToSearchValueTypeMap _searchParameterTypeMap;

        public SqlBulkCopyDataWrapperFactory(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
        {
            _model = model;
            _searchParameterTypeMap = searchParameterTypeMap;
        }

        public SqlBulkCopyDataWrapper CreateSqlBulkCopyDataWrapper(BulkImportResourceWrapper resource)
        {
            var resourceMetadata = new ResourceMetadata(
                resource.Resource.CompartmentIndices,
                resource.Resource.SearchIndices?.ToLookup(e => _searchParameterTypeMap.GetSearchValueType(e)),
                resource.Resource.LastModifiedClaims);
            short resourceTypeId = _model.GetResourceTypeId(resource.Resource.ResourceTypeName);

            return new SqlBulkCopyDataWrapper()
            {
                Metadata = resourceMetadata,
                ResourceTypeId = resourceTypeId,
                Resource = resource.Resource,
                ResourceSurrogateId = resource.ResourceSurrogateId,
                CompressedRawData = resource.CompressedRawData,
            };
        }
    }
}
