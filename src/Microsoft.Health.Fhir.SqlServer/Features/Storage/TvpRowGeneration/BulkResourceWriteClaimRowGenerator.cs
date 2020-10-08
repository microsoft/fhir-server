// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkResourceWriteClaimRowGenerator : ITableValuedParameterRowGenerator<IReadOnlyList<ResourceMetadata>, VLatest.BulkResourceWriteClaimTableTypeRow>
    {
        private readonly SqlServerFhirModel _model;

        public BulkResourceWriteClaimRowGenerator(SqlServerFhirModel model)
        {
            EnsureArg.IsNotNull(model, nameof(model));
            _model = model;
        }

        public IEnumerable<VLatest.BulkResourceWriteClaimTableTypeRow> GenerateRows(IReadOnlyList<ResourceMetadata> resources)
        {
            for (var index = 0; index < resources.Count; index++)
            {
                ResourceMetadata resourceMetadata = resources[index];
                if (resourceMetadata.WriteClaims != null)
                {
                    foreach ((string key, string value) in resourceMetadata.WriteClaims)
                    {
                        yield return new VLatest.BulkResourceWriteClaimTableTypeRow(index, _model.GetClaimTypeId(key), value);
                    }
                }
            }
        }
    }
}
