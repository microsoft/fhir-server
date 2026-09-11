// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.IO;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration.Merge;
using Microsoft.Health.SqlServer.Features.Schema.Model;
using Microsoft.IO;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal sealed class VectorSearchParamListRowGenerator : ITableValuedParameterRowGenerator<IReadOnlyList<MergeResourceWrapper>, VectorSearchParamListRow>
    {
        private readonly SqlServerFhirModel _model;
        private readonly ICompressedRawResourceConverter _compressedRawResourceConverter;
        private readonly RecyclableMemoryStreamManager _memoryStreamManager;

        public VectorSearchParamListRowGenerator(
            SqlServerFhirModel model,
            ICompressedRawResourceConverter compressedRawResourceConverter)
        {
            _model = EnsureArg.IsNotNull(model, nameof(model));
            _compressedRawResourceConverter = EnsureArg.IsNotNull(compressedRawResourceConverter, nameof(compressedRawResourceConverter));
            _memoryStreamManager = new RecyclableMemoryStreamManager();
        }

        public IEnumerable<VectorSearchParamListRow> GenerateRows(IReadOnlyList<MergeResourceWrapper> resources)
        {
            EnsureArg.IsNotNull(resources, nameof(resources));

            var keys = new HashSet<(short ResourceTypeId, long ResourceSurrogateId, short SearchParamId, short ChunkOrdinal)>();
            foreach (MergeResourceWrapper merge in resources.Where(resource => !resource.ResourceWrapper.IsHistory))
            {
                short resourceTypeId = _model.GetResourceTypeId(merge.ResourceWrapper.ResourceTypeName);

                foreach (VectorSearchIndexEntry vectorIndex in merge.ResourceWrapper.VectorSearchIndices)
                {
                    short searchParamId = _model.GetSearchParamId(vectorIndex.SearchParameter.Url);

                    foreach (VectorSearchChunk chunk in vectorIndex.Chunks)
                    {
                        short chunkOrdinal = checked((short)chunk.ChunkOrdinal);
                        if (!keys.Add((resourceTypeId, merge.ResourceWrapper.ResourceSurrogateId, searchParamId, chunkOrdinal)))
                        {
                            continue;
                        }

                        using var stream = new RecyclableMemoryStream(_memoryStreamManager, tag: nameof(VectorSearchParamListRowGenerator));
                        _compressedRawResourceConverter.WriteCompressedRawResource(stream, chunk.ChunkText);
                        stream.Seek(0, SeekOrigin.Begin);

                        yield return new VectorSearchParamListRow(
                            resourceTypeId,
                            merge.ResourceWrapper.ResourceSurrogateId,
                            searchParamId,
                            chunkOrdinal,
                            vectorIndex.EmbeddingModelId,
                            ToArray(chunk.SourceTextHash),
                            stream,
                            SqlVectorFormatter.Format(chunk.Embedding));
                    }
                }
            }
        }

        private static byte[] ToArray(IReadOnlyList<byte> source)
        {
            var result = new byte[source.Count];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = source[index];
            }

            return result;
        }
    }
}
