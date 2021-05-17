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
   internal class BulkReindexResourceV1RowGenerator : ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, BulkReindexResourceTableTypeV1Row>
    {
        private readonly ISqlServerFhirModel _model;

        public BulkReindexResourceV1RowGenerator(ISqlServerFhirModel model)
        {
            EnsureArg.IsNotNull(model, nameof(model));
            _model = model;
        }

        public IEnumerable<BulkReindexResourceTableTypeV1Row> GenerateRows(IReadOnlyList<ResourceWrapper> input)
        {
            for (var index = 0; index < input.Count; index++)
            {
                ResourceWrapper resource = input[index];
                var resourceTypeId = _model.GetResourceTypeId(resource.ResourceTypeName);
                var resourceId = resource.ResourceId;

                int etag = 0;
                if (resource.Version != null && !int.TryParse(resource.Version, out etag))
                {
                    // Set the etag to a sentinel value to enable expected failure paths when updating with both existing and nonexistent resources.
                    etag = -1;
                }

                yield return new BulkReindexResourceTableTypeV1Row(
                    index,
                    resourceTypeId,
                    resourceId,
                    resource.Version == null ? null : etag,
                    resource.SearchParameterHash);
            }
        }
    }
}
