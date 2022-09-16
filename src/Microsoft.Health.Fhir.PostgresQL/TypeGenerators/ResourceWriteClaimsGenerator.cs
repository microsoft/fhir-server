// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using static Microsoft.Health.Fhir.PostgresQL.TypeConvert;

namespace Microsoft.Health.Fhir.PostgresQL.TypeGenerators
{
    internal class ResourceWriteClaimsGenerator
    {
        private readonly ISqlServerFhirModel _model;

        public ResourceWriteClaimsGenerator(ISqlServerFhirModel model)
        {
            EnsureArg.IsNotNull(model, nameof(model));

            _model = model;
        }

        public IEnumerable<BulkResourceWriteClaimTableTypeV1Row> GenerateRows(IReadOnlyList<ResourceWrapper> resources)
        {
            for (var index = 0; index < resources.Count; index++)
            {
                ResourceWrapper resource = resources[index];

                IReadOnlyCollection<KeyValuePair<string, string>> writeClaims = resource.LastModifiedClaims;
                if (writeClaims == null)
                {
                    yield break;
                }

                foreach (var claim in writeClaims)
                {
                    yield return new BulkResourceWriteClaimTableTypeV1Row()
                    {
                        Offset = index,
                        claimtypeid = _model.GetClaimTypeId(claim.Key),
                        claimvalue = claim.Value,
                    };
                }
            }
        }
    }
}
