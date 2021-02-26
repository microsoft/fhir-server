// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.IO;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public class ProcessFhirResourceStep : IStep
    {
        private static readonly Encoding ResourceEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        private const int MaxBatchSize = 1000;
        private const int ConcurrentLimit = 8;

        private Channel<string> _input;
        private Channel<BulkCopyResourceWrapper> _resourceOutput;
        private Channel<BulkCopySearchParamWrapper> _searchParamOutput;
        private Task _runningTask;
        private List<string> _buffer = new List<string>();
        private Queue<Task<IReadOnlyCollection<(ResourceElement, byte[], IReadOnlyCollection<SearchIndexEntry>)>>> _processingTasks = new Queue<Task<IReadOnlyCollection<(ResourceElement, byte[], IReadOnlyCollection<SearchIndexEntry>)>>>();
        private ISearchIndexer _searchIndexer;
        private IRawResourceFactory _rawResourceFactory;
        private RecyclableMemoryStreamManager _memoryStreamManager;
        private long _currentSurrogateId;

        public ProcessFhirResourceStep(
            Channel<string> input,
            Channel<BulkCopyResourceWrapper> resourceOutput,
            Channel<BulkCopySearchParamWrapper> searchParamOutput,
            ISearchIndexer searchIndexer,
            IRawResourceFactory rawResourceFactory)
        {
            _input = input;
            _resourceOutput = resourceOutput;
            _searchParamOutput = searchParamOutput;
            _searchIndexer = searchIndexer;
            _rawResourceFactory = rawResourceFactory;
            _currentSurrogateId = LastUpdatedToResourceSurrogateId(DateTime.UtcNow);
            _memoryStreamManager = new RecyclableMemoryStreamManager();
        }

        public void Start()
        {
            _runningTask = Task.Run(async () =>
            {
                FhirJsonParser parser = new FhirJsonParser();

                while (await _input.Reader.WaitToReadAsync())
                {
                    await foreach (string content in _input.Reader.ReadAllAsync())
                    {
                        _buffer.Add(content);
                        if (_buffer.Count < MaxBatchSize)
                        {
                            continue;
                        }

                        if (_processingTasks.Count >= ConcurrentLimit)
                        {
                            await WaitForOneTaskCompleteAsync();
                        }

                        string[] contents = _buffer.ToArray();
                        _buffer.Clear();
                        _processingTasks.Enqueue(ProcessRawDataAsync(contents, parser));
                    }
                }

                _processingTasks.Enqueue(ProcessRawDataAsync(_buffer.ToArray(), parser));
                while (_processingTasks.Count() > 0)
                {
                    await WaitForOneTaskCompleteAsync();
                }

                _resourceOutput.Writer.Complete();
                _searchParamOutput.Writer.Complete();
            });
        }

        private async Task<IReadOnlyCollection<(ResourceElement, byte[], IReadOnlyCollection<SearchIndexEntry>)>> ProcessRawDataAsync(string[] contents, FhirJsonParser parser)
        {
            return await Task.Run(() =>
            {
                List<(ResourceElement, byte[], IReadOnlyCollection<SearchIndexEntry>)> result = new List<(ResourceElement, byte[], IReadOnlyCollection<SearchIndexEntry>)>();
                foreach (string content in contents)
                {
                    Resource resource = parser.Parse<Resource>(content);
                    ITypedElement element = resource.ToTypedElement();
                    ResourceElement resourceElement = new ResourceElement(element);
                    IReadOnlyCollection<SearchIndexEntry> searchIndexEntry = _searchIndexer.Extract(resourceElement);
                    string rawResourceString = GetRawDataString(resourceElement, true);
                    byte[] rawData = WriteCompressedRawResource(rawResourceString);
                    result.Add((resourceElement, rawData, searchIndexEntry));
                }

                return result;
            });
        }

        private async Task WaitForOneTaskCompleteAsync()
        {
            if (_processingTasks.Count() > 0)
            {
                IReadOnlyCollection<(ResourceElement, byte[], IReadOnlyCollection<SearchIndexEntry>)> processResult = await _processingTasks.Dequeue().ConfigureAwait(false);
                foreach (var processedData in processResult)
                {
                    Interlocked.Increment(ref _currentSurrogateId);
                    var item = new BulkCopyResourceWrapper(processedData.Item1, processedData.Item2, _currentSurrogateId);

                    await _resourceOutput.Writer.WriteAsync(item);

                    foreach (var searchParam in processedData.Item3)
                    {
                        await _searchParamOutput.Writer.WriteAsync(new BulkCopySearchParamWrapper(processedData.Item1, searchParam, _currentSurrogateId));
                    }
                }

                Console.WriteLine($"{_currentSurrogateId} parsed.");
            }
        }

        public static long LastUpdatedToResourceSurrogateId(DateTime dateTime)
        {
            long id = dateTime.TruncateToMillisecond().Ticks << 3;

            return id;
        }

        public async Task WaitForStopAsync()
        {
            await _runningTask;
        }

        private string GetRawDataString(ResourceElement resource, bool keepMeta)
        {
            RawResource rawResource = _rawResourceFactory.Create(resource, keepMeta);
            return rawResource.Data;
        }

        private byte[] WriteCompressedRawResource(string rawResource)
        {
            using var stream = new RecyclableMemoryStream(_memoryStreamManager);

            using var gzipStream = new GZipStream(stream, CompressionMode.Compress, leaveOpen: true);
            using var writer = new StreamWriter(gzipStream, ResourceEncoding);
            writer.Write(rawResource);
            writer.Flush();
            stream.Seek(0, 0);

            return stream.ToArray();
        }
    }
}
