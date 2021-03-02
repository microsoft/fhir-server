// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Fhir.BulkImportDemoWorker.SearchParamGenerator;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public class ImportSearchParamStep : IStep<long>
    {
        private const int MaxBatchSize = 10000;
        private const int ConcurrentLimit = 5;

        private Task _runningTask;
        private Dictionary<string, List<BulkCopySearchParamWrapper>> _buffer;
        private Channel<BulkCopySearchParamWrapper> _input;
        private Dictionary<string, ISearchParamGenerator> _generators;
        private ModelProvider _provider;
        private IConfiguration _configuration;
        private long _processedCount = 0;
        private Queue<Task> _bulkCopyTasks = new Queue<Task>();

        public ImportSearchParamStep(
            Channel<BulkCopySearchParamWrapper> input,
            ModelProvider provider,
            IConfiguration configuration)
        {
            _buffer = new Dictionary<string, List<BulkCopySearchParamWrapper>>();
            _input = input;
            _provider = provider;
            _configuration = configuration;

            InitializeSearchParamGenerator();
        }

        public void Start(IProgress<long> progress)
        {
            _runningTask = Task.Run(async () =>
            {
                while (await _input.Reader.WaitToReadAsync())
                {
                    await foreach (BulkCopySearchParamWrapper resource in _input.Reader.ReadAllAsync())
                    {
                        var parameterType = resource.SearchIndexEntry.SearchParameter.Type.ToString();
                        if (parameterType.ToUpperInvariant() == "COMPOSITE")
                        {
                            parameterType = ResloveCompositeType((CompositeSearchValue)resource.SearchIndexEntry.Value);
                        }

                        if (!_generators.ContainsKey(parameterType))
                        {
                            continue; // TODO: we should throw exception for not support later.
                        }

                        if (!_buffer.ContainsKey(parameterType))
                        {
                            _buffer[parameterType] = new List<BulkCopySearchParamWrapper>();
                        }

                        _buffer[parameterType].Add(resource);
                        if (_buffer[parameterType].Count < MaxBatchSize)
                        {
                            continue;
                        }

                        if (_bulkCopyTasks.Count >= ConcurrentLimit)
                        {
                            await _bulkCopyTasks.Dequeue();
                        }

                        BulkCopySearchParamWrapper[] items = _buffer[parameterType].ToArray();
                        _buffer[parameterType].Clear();

                        _bulkCopyTasks.Enqueue(BulkCopyToSearchParamTableAsync(parameterType, items, progress));
                    }

                    foreach (var searchParams in _buffer)
                    {
                        _bulkCopyTasks.Enqueue(BulkCopyToSearchParamTableAsync(searchParams.Key, searchParams.Value.ToArray(), progress));
                    }

                    Task.WaitAll(_bulkCopyTasks.ToArray());
                }
            });
        }

        public async Task WaitForStopAsync()
        {
            await _runningTask;
        }

        private async Task BulkCopyToSearchParamTableAsync(string parameterType, BulkCopySearchParamWrapper[] items, IProgress<long> progress)
        {
            ISearchParamGenerator generator = _generators[parameterType];
            using (SqlConnection destinationConnection =
                       new SqlConnection(_configuration["SqlConnectionString"]))
            {
                destinationConnection.Open();

                DataTable importTable = generator.CreateDataTable();
                foreach (var searchItem in items)
                {
                    DataRow row = generator.GenerateDataRow(importTable, searchItem);
                    if (row != null)
                    {
                        importTable.Rows.Add(row);
                    }
                }

                using IDataReader reader = importTable.CreateDataReader();
                using (SqlBulkCopy bulkCopy =
                            new SqlBulkCopy(destinationConnection))
                {
                    try
                    {
                        bulkCopy.DestinationTableName =
                            generator.TableName;
                        await bulkCopy.WriteToServerAsync(reader);

                        Interlocked.Add(ref _processedCount, items.Length);
                        progress.Report(_processedCount);
                        Console.WriteLine($"{_processedCount} {parameterType.ToString()} search params to db completed.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                    finally
                    {
                        destinationConnection.Close();
                    }
                }
            }
        }

        private static string ResloveCompositeType(CompositeSearchValue compositeSearchValue)
        {
            var components = compositeSearchValue.Components;
            var res = string.Empty;

            foreach (var component in components)
            {
                string type = component[0].GetType().ToString();
                type = type.Split(".").Last();
                type = type.Substring(0, type.Length - 11); // remove character search value
                for (int i = 0; i < component.Count; i++)
                {
                    res += type;
                }
            }

            return res + "Composite";
        }

        private void InitializeSearchParamGenerator()
        {
            _generators = new Dictionary<string, ISearchParamGenerator>()
            {
                { SearchParamType.String.ToString(), new StringSearchParamGenerator(_provider) },
                { SearchParamType.Number.ToString(), new NumberSearchParamGenerator(_provider) },
                { SearchParamType.Uri.ToString(), new UriSearchParamGenerator(_provider) },
                { SearchParamType.Date.ToString(), new DateSearchParamGenerator(_provider) },
                { SearchParamType.Token.ToString(), new TokenSearchParamGenerator(_provider) },
                { SearchParamType.Quantity.ToString(), new QuantitySearchParamGenerator(_provider) },
                { SearchParamType.Reference.ToString(), new ReferenceSearchParamGenerator(_provider) },
                { SearchParamType.Special.ToString(), new StringSearchParamGenerator(_provider) },
                { "TokenTokenComposite", new TokenTokenCompositeSearchParamGenerator(_provider) },
                { "ReferenceTokenComposite", new ReferenceTokenCompositeSearchParamGenerator(_provider) },
                { "TokenDateTimeComposite", new TokenDateTimeCompositeSearchParamGenerator(_provider) },
                { "TokenNumberNumberComposite", new TokenNumberNumberCompositeSearchParamGenerator(_provider) },
                { "TokenQuantityComposite", new TokenQuantityCompositeSearchParamGenerator(_provider) },
                { "TokenStringComposite", new TokenStringCompositeSearchParamGenerator(_provider) },
            };
        }
    }
}
