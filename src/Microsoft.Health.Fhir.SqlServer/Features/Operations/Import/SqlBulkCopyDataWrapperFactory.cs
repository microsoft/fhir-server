// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import
{
    internal class SqlBulkCopyDataWrapperFactory : ISqlBulkCopyDataWrapperFactory
    {
        private SqlServerFhirModel _model;
        private SearchParameterToSearchValueTypeMap _searchParameterTypeMap;

        public SqlBulkCopyDataWrapperFactory(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
        {
            EnsureArg.IsNotNull(model, nameof(model));
            EnsureArg.IsNotNull(searchParameterTypeMap, nameof(searchParameterTypeMap));

            _model = model;
            _searchParameterTypeMap = searchParameterTypeMap;
        }

        public SqlBulkCopyDataWrapper CreateSqlBulkCopyDataWrapper(ImportResource resource)
        {
            var resourceMetadata = new ResourceMetadata(
                resource.Resource.CompartmentIndices,
                resource.Resource.SearchIndices?.ToLookup(e => _searchParameterTypeMap.GetSearchValueType(e)),
                resource.Resource.LastModifiedClaims);
            short resourceTypeId = _model.GetResourceTypeId(resource.Resource.ResourceTypeName);

            resource.CompressedStream.Seek(0, 0);

            return new SqlBulkCopyDataWrapper()
            {
                Metadata = resourceMetadata,
                ResourceTypeId = resourceTypeId,
                Resource = resource.Resource,
                ResourceSurrogateId = resource.Id,
                Index = resource.Index,
                BulkImportResource = resource.ExtractBulkImportResourceTypeV1Row(resourceTypeId),
            };
        }

        public async Task EnsureInitializedAsync()
        {
            await _model.EnsureInitialized();
        }
    }
}
