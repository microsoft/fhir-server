// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration.Merge;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal sealed class VectorSearchParamListRowGenerator : ITableValuedParameterRowGenerator<IReadOnlyList<MergeResourceWrapper>, VectorSearchParamListRow>
    {
        private readonly SqlServerFhirModel _model;

        public VectorSearchParamListRowGenerator(SqlServerFhirModel model)
        {
            _model = EnsureArg.IsNotNull(model, nameof(model));
        }

        public IEnumerable<VectorSearchParamListRow> GenerateRows(IReadOnlyList<MergeResourceWrapper> resources)
        {
            EnsureArg.IsNotNull(resources, nameof(resources));

            foreach (MergeResourceWrapper merge in resources.Where(resource => !resource.ResourceWrapper.IsHistory))
            {
                short resourceTypeId = _model.GetResourceTypeId(merge.ResourceWrapper.ResourceTypeName);

                foreach (VectorSearchIndexEntry vectorIndex in merge.ResourceWrapper.VectorSearchIndices)
                {
                    short searchParamId = _model.GetSearchParamId(vectorIndex.SearchParameter.Url);

                    foreach (VectorSearchChunk chunk in vectorIndex.Chunks)
                    {
                        yield return new VectorSearchParamListRow(
                            resourceTypeId,
                            merge.ResourceWrapper.ResourceSurrogateId,
                            searchParamId,
                            checked((short)chunk.ChunkOrdinal),
                            vectorIndex.EmbeddingModelId,
                            chunk.ChunkText,
                            ToArray(chunk.SourceTextHash),
                            _model.GetResourceTypeId(chunk.SourceResourceType ?? merge.ResourceWrapper.ResourceTypeName),
                            chunk.SourceResourceId ?? merge.ResourceWrapper.ResourceId,
                            chunk.SourceResourceVersion ?? merge.ResourceWrapper.Version,
                            chunk.SourcePath ?? vectorIndex.SearchParameter.Expression ?? vectorIndex.SearchParameter.Code,
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
