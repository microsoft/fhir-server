// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class ResourceWriteClaimV1RowGenerator : ITableValuedParameterRowGenerator<ResourceMetadata, ResourceWriteClaimTableTypeV1Row>
    {
        private readonly ISqlServerFhirModel _model;

        public ResourceWriteClaimV1RowGenerator(ISqlServerFhirModel model)
        {
            EnsureArg.IsNotNull(model, nameof(model));
            _model = model;
        }

        public IEnumerable<ResourceWriteClaimTableTypeV1Row> GenerateRows(ResourceMetadata resourceMetadata)
        {
            return resourceMetadata.WriteClaims?.Select(c =>
                new ResourceWriteClaimTableTypeV1Row(_model.GetClaimTypeId(c.Key), c.Value));
        }
    }
}
