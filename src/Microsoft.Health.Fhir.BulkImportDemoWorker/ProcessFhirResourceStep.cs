// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public class ProcessFhirResourceStep : IStep
    {
        private const int BatchSize = 1000;
        private const int MaxParallelCount = 8;

        private Channel<string> _input;
        private Channel<BulkCopyResourceWrapper> _resourceOutput;
        private Dictionary<ValueSets.SearchParamType, Channel<BulkCopySearchParamWrapper>> _searchParamOutputs;
        private Task _runningTask;
        private List<string> _buffer = new List<string>();
        private Queue<Task<IReadOnlyCollection<(ResourceElement, IReadOnlyCollection<SearchIndexEntry>)>>> _processingTasks = new Queue<Task<IReadOnlyCollection<(ResourceElement, IReadOnlyCollection<SearchIndexEntry>)>>>();
        private ISearchIndexer _searchIndexer;
        private long _currentSurrogateId;

        public ProcessFhirResourceStep(
            Channel<string> input,
            Channel<BulkCopyResourceWrapper> resourceOutput,
            Dictionary<ValueSets.SearchParamType, Channel<BulkCopySearchParamWrapper>> searchParamOutputs,
            ISearchIndexer searchIndexer)
        {
            _input = input;
            _resourceOutput = resourceOutput;
            _searchParamOutputs = searchParamOutputs;
            _searchIndexer = searchIndexer;
            _currentSurrogateId = LastUpdatedToResourceSurrogateId(DateTime.UtcNow);
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
                        if (_buffer.Count < BatchSize)
                        {
                            _buffer.Add(content);
                            continue;
                        }

                        if (_processingTasks.Count >= MaxParallelCount)
                        {
                            await WaitForOneTaskCompleteAsync();
                        }

                        _processingTasks.Enqueue(ProcessRawDataAsync(_buffer.ToArray(), parser));
                        _buffer.Clear();
                    }
                }

                _processingTasks.Enqueue(ProcessRawDataAsync(_buffer.ToArray(), parser));
                while (_processingTasks.Count() > 0)
                {
                    await WaitForOneTaskCompleteAsync();
                }

                _resourceOutput.Writer.Complete();
            });
        }

        private async Task<IReadOnlyCollection<(ResourceElement, IReadOnlyCollection<SearchIndexEntry>)>> ProcessRawDataAsync(string[] contents, FhirJsonParser parser)
        {
            return await Task.Run(() =>
            {
                List<(ResourceElement, IReadOnlyCollection<SearchIndexEntry>)> result = new List<(ResourceElement, IReadOnlyCollection<SearchIndexEntry>)>();
                foreach (string content in contents)
                {
                    Resource resource = parser.Parse<Resource>(content);
                    ITypedElement element = resource.ToTypedElement();
                    ResourceElement resourceElement = new ResourceElement(element);
                    IReadOnlyCollection<SearchIndexEntry> searchIndexEntry = _searchIndexer.Extract(resourceElement);

                    result.Add((resourceElement, searchIndexEntry));
                }

                return result;
            });
        }

        private async Task WaitForOneTaskCompleteAsync()
        {
            if (_processingTasks.Count() > 0)
            {
                IReadOnlyCollection<(ResourceElement, IReadOnlyCollection<SearchIndexEntry>)> processResult = await _processingTasks.Dequeue().ConfigureAwait(false);
                foreach (var processedData in processResult)
                {
                    Interlocked.Increment(ref _currentSurrogateId);

                    await _resourceOutput.Writer.WriteAsync(new BulkCopyResourceWrapper(processedData.Item1, _currentSurrogateId));

                    foreach (var searchParam in processedData.Item2)
                    {
                        if (_searchParamOutputs.ContainsKey(searchParam.SearchParameter.Type))
                        {
                            await _searchParamOutputs[searchParam.SearchParameter.Type].Writer.WriteAsync(new BulkCopySearchParamWrapper(processedData.Item1, searchParam, _currentSurrogateId));
                        }
                    }
                }
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
    }
}
