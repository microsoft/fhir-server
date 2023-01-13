// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class ResourceListRowGenerator : ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, ResourceListRow>
    {
        private readonly ISqlServerFhirModel _model;
        private readonly ICompressedRawResourceConverter _compressedRawResourceConverter;

        public ResourceListRowGenerator(ISqlServerFhirModel model, ICompressedRawResourceConverter compressedRawResourceConverter)
        {
            _model = EnsureArg.IsNotNull(model, nameof(model));
            _compressedRawResourceConverter = EnsureArg.IsNotNull(compressedRawResourceConverter, nameof(compressedRawResourceConverter));
        }

        public IEnumerable<ResourceListRow> GenerateRows(IReadOnlyList<ResourceWrapper> resources)
        {
            // This logic currently works only for single resource version and it does not preserve surrogate id
            var resourceRecordId = 0L;
            foreach (var resource in resources)
            {
                var stream = new MemoryStream();
                _compressedRawResourceConverter.WriteCompressedRawResource(stream, resource.RawResource.Data);
                stream.Seek(0, 0);
                yield return new ResourceListRow(_model.GetResourceTypeId(resource.ResourceTypeName), resourceRecordId, resource.ResourceId, int.Parse(resource.Version), true, resource.IsDeleted, resource.IsHistory, stream, resource.Request.Method, resource.SearchParameterHash);
                resourceRecordId++;
            }
        }
    }
}
