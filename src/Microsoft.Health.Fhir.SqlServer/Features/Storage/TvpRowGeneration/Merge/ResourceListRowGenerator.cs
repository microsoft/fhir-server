// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics;
using EnsureThat;
using Microsoft.Extensions.Logging;
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
        private readonly ILogger<ResourceListRowGenerator> _logger;

        public ResourceListRowGenerator(ISqlServerFhirModel model, ICompressedRawResourceConverter compressedRawResourceConverter, ILogger<ResourceListRowGenerator> logger)
        {
            _model = EnsureArg.IsNotNull(model, nameof(model));
            _compressedRawResourceConverter = EnsureArg.IsNotNull(compressedRawResourceConverter, nameof(compressedRawResourceConverter));
            _memoryStreamManager = new RecyclableMemoryStreamManager();
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public IEnumerable<ResourceListRow> GenerateRows(IReadOnlyList<MergeResourceWrapper> mergeWrappers)
        {
            foreach (var merge in mergeWrappers)
            {
                _logger.LogInformation("Profiling - Starting the compression");
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();
                var wrapper = merge.ResourceWrapper;
                using var stream = new RecyclableMemoryStream(_memoryStreamManager, tag: nameof(ResourceListRowGenerator));
                _compressedRawResourceConverter.WriteCompressedRawResource(stream, wrapper.RawResource.Data);
                stream.Seek(0, 0);
                _logger.LogInformation($"Profiling - Ending the compression {stopwatch.ElapsedMilliseconds}");
                stopwatch.Stop();

                // Specify the file path
                _logger.LogInformation("Profiling - Starting the compression and writing to file");
                stopwatch.Start();
                string filePath = "compressed_data.txt";

                // Write compressed data to file
                _compressedRawResourceConverter.WriteCompressedDataToFile(filePath, wrapper.RawResource.Data);
                _logger.LogInformation($"Profiling - Ending the compression and writing to file {stopwatch.ElapsedMilliseconds}");
                stopwatch.Stop();

                // Specify the file path
                _logger.LogInformation("Profiling - Starting the compression and writing to file with Bytes");
                stopwatch.Start();
                string filePath1 = "compressed_data1.txt";

                // Write compressed data to file
                _compressedRawResourceConverter.CompressAndWriteToFileWithBytes(filePath1, wrapper.RawResource.Data);
                _logger.LogInformation($"Profiling - Ending the compression and writing to file with Bytes {stopwatch.ElapsedMilliseconds}");
                stopwatch.Stop();

                yield return new ResourceListRow(_model.GetResourceTypeId(wrapper.ResourceTypeName), merge.ResourceWrapper.ResourceSurrogateId, wrapper.ResourceId, int.Parse(wrapper.Version), merge.HasVersionToCompare, wrapper.IsDeleted, wrapper.IsHistory, merge.KeepHistory, stream, wrapper.RawResource.IsMetaSet, wrapper.Request?.Method, wrapper.SearchParameterHash);
            }
        }
    }
}
