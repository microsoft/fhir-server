// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration.Merge;
using Microsoft.Health.SqlServer.Features.Schema.Model;
using Microsoft.IO;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class ResourceListRowGenerator : ITableValuedParameterRowGenerator<IReadOnlyList<MergeResourceWrapper>, ResourceListRow>
    {
        private readonly ISqlServerFhirModel _model;
        private readonly ICompressedRawResourceConverter _compressedRawResourceConverter;
        private readonly RecyclableMemoryStreamManager _memoryStreamManager;

        public ResourceListRowGenerator(ISqlServerFhirModel model, ICompressedRawResourceConverter compressedRawResourceConverter)
        {
            _model = EnsureArg.IsNotNull(model, nameof(model));
            _compressedRawResourceConverter = EnsureArg.IsNotNull(compressedRawResourceConverter, nameof(compressedRawResourceConverter));
            _memoryStreamManager = new RecyclableMemoryStreamManager();
        }

        public IEnumerable<ResourceListRow> GenerateRows(IReadOnlyList<MergeResourceWrapper> mergeWrappers)
        {
            foreach (var merge in mergeWrappers)
            {
                var wrapper = merge.ResourceWrapper;
                using var stream = new RecyclableMemoryStream(_memoryStreamManager, tag: nameof(ResourceListRowGenerator));
                _compressedRawResourceConverter.WriteCompressedRawResource(stream, wrapper.RawResource.Data);
                stream.Seek(0, 0);
                yield return new ResourceListRow(_model.GetResourceTypeId(wrapper.ResourceTypeName), merge.ResourceWrapper.ResourceSurrogateId, wrapper.ResourceId, int.Parse(wrapper.Version), merge.HasVersionToCompare, wrapper.IsDeleted, wrapper.IsHistory, merge.KeepHistory, stream, wrapper.RawResource.IsMetaSet, wrapper.Request.Method, wrapper.SearchParameterHash);
            }
        }
    }
}
