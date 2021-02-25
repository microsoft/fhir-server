// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public class ImportResourceTableStep : IStep
    {
        private const int MaxBatchSize = 1000;
        private const int ConcurrentLimit = 20;

        private long copiedCount = 0;
        private Channel<BulkCopyResourceWrapper> _input;
        private Task _runningTask;
        private ResourceIdProvider _resourceIdProvider;
        private IConfiguration _configuration;
        private Dictionary<string, short> _resourceTypeMappings;
        private Queue<Task> _runningTasks = new Queue<Task>();
        private List<BulkCopyResourceWrapper> _buffer = new List<BulkCopyResourceWrapper>();

        public ImportResourceTableStep(
            Channel<BulkCopyResourceWrapper> input,
            ResourceIdProvider resourceIdProvider,
            ModelProvider modelProvider,
            IConfiguration configuration)
        {
            _input = input;
            _resourceIdProvider = resourceIdProvider;
            _configuration = configuration;
            _resourceTypeMappings = modelProvider.ResourceTypeMapping;
        }

        public void Start()
        {
            _runningTask = Task.Run(async () =>
            {
                while (await _input.Reader.WaitToReadAsync())
                {
                    await foreach (BulkCopyResourceWrapper resource in _input.Reader.ReadAllAsync())
                    {
                        _buffer.Add(resource);

                        if (_buffer.Count < MaxBatchSize)
                        {
                            continue;
                        }

                        if (_runningTasks.Count >= ConcurrentLimit)
                        {
                            await _runningTasks.Dequeue();
                        }

                        var items = _buffer.ToArray();
                        _buffer.Clear();
                        _runningTasks.Enqueue(ProcessResourceElementsInBufferAsync(items));
                    }
                }

                _runningTasks.Enqueue(ProcessResourceElementsInBufferAsync(_buffer.ToArray()));

                while (_runningTasks.Count > 0)
                {
                    await _runningTasks.Dequeue();
                }
            });
        }

        public async Task WaitForStopAsync()
        {
            await _runningTask;
        }

        private async Task ProcessResourceElementsInBufferAsync(BulkCopyResourceWrapper[] data)
        {
            using SqlConnection destinationConnection =
                       new SqlConnection(_configuration["SqlConnectionString"]);
            destinationConnection.Open();

            DataTable importTable = CreateDataTable();
            foreach (var resourceElement in data)
            {
                short resourceTypeId = _resourceTypeMappings[resourceElement.Resource.InstanceType];
                string resourceId = _resourceIdProvider.Create();
                long resourceSurrogateId = resourceElement.SurrogateId;

                DataRow row = importTable.NewRow();
                row["ResourceTypeId"] = resourceTypeId;
                row["ResourceId"] = resourceId;
                row["Version"] = 1;
                row["IsHistory"] = false;
                row["ResourceSurrogateId"] = resourceSurrogateId;
                row["IsDeleted"] = false;
                row["RequestMethod"] = "POST";
                row["RawResource"] = resourceElement.RawData;
                row["IsRawResourceMetaSet"] = true;
                importTable.Rows.Add(row);
            }

            using IDataReader reader = importTable.CreateDataReader();
            using (SqlBulkCopy bulkCopy =
                        new SqlBulkCopy(destinationConnection))
            {
                try
                {
                    bulkCopy.DestinationTableName = "dbo.Resource";
                    await bulkCopy.WriteToServerAsync(reader);

                    Interlocked.Add(ref copiedCount, importTable.Rows.Count);
                    Console.WriteLine($"{copiedCount} resource to db completed.");
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

        private static DataTable CreateDataTable()
        {
            // Create a new DataTable.
            DataTable table = new DataTable("DataTable");
            DataColumn column;

            column = new DataColumn();
            column.DataType = typeof(short);
            column.ColumnName = "ResourceTypeId";
            column.ReadOnly = true;
            table.Columns.Add(column);

            column = new DataColumn();
            column.DataType = typeof(string);
            column.ColumnName = "ResourceId";
            column.ReadOnly = true;
            table.Columns.Add(column);

            column = new DataColumn();
            column.DataType = typeof(int);
            column.ColumnName = "Version";
            column.ReadOnly = true;
            table.Columns.Add(column);

            column = new DataColumn();
            column.DataType = typeof(bool);
            column.ColumnName = "IsHistory";
            column.ReadOnly = true;
            table.Columns.Add(column);

            column = new DataColumn();
            column.DataType = typeof(long);
            column.ColumnName = "ResourceSurrogateId";
            column.ReadOnly = true;
            table.Columns.Add(column);

            column = new DataColumn();
            column.DataType = typeof(bool);
            column.ColumnName = "IsDeleted";
            column.ReadOnly = true;
            table.Columns.Add(column);

            column = new DataColumn();
            column.DataType = typeof(string);
            column.ColumnName = "RequestMethod";
            column.ReadOnly = true;
            table.Columns.Add(column);

            column = new DataColumn();
            column.DataType = typeof(byte[]);
            column.ColumnName = "RawResource";
            column.ReadOnly = true;
            table.Columns.Add(column);

            column = new DataColumn();
            column.DataType = typeof(bool);
            column.ColumnName = "IsRawResourceMetaSet";
            column.ReadOnly = true;
            table.Columns.Add(column);

            return table;
        }
    }
}
