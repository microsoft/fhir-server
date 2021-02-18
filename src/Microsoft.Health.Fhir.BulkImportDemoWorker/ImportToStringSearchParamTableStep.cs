// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public class ImportToStringSearchParamTableStep : IStep
    {
        private const int BatchSize = 20000;

        private Channel<BulkCopySearchParamWrapper> _input;
        private Dictionary<string, short> _resourceTypeMappings;
        private IConfiguration _configuration;
        private List<BulkCopySearchParamWrapper> _buffer = new List<BulkCopySearchParamWrapper>();
        private Task _runningTask;

        public ImportToStringSearchParamTableStep(
            Channel<BulkCopySearchParamWrapper> input,
            Dictionary<string, short> resourceTypeMappings,
            IConfiguration configuration)
        {
            _input = input;
            _resourceTypeMappings = resourceTypeMappings;
            _configuration = configuration;
        }

        public void Start()
        {
            _runningTask = Task.Run(async () =>
            {
                using (SqlConnection destinationConnection =
                       new SqlConnection(_configuration["SqlConnectionString"]))
                {
                    destinationConnection.Open();
                    while (await _input.Reader.WaitToReadAsync())
                    {
                        await foreach (BulkCopySearchParamWrapper resource in _input.Reader.ReadAllAsync())
                        {
                            if (_buffer.Count < BatchSize)
                            {
                                _buffer.Add(resource);
                                continue;
                            }

                            await ProcessResourceElementsInBufferAsync(destinationConnection);
                        }
                    }

                    await ProcessResourceElementsInBufferAsync(destinationConnection);
                }
            });
        }

        public async Task WaitForStopAsync()
        {
            await _runningTask;
        }

        private async Task ProcessResourceElementsInBufferAsync(SqlConnection destinationConnection)
        {
            var data = _buffer.ToArray();
            _buffer.Clear();

            DataTable importTable = CreateDataTable();
            foreach (var searchItem in data)
            {
                short resourceTypeId = _resourceTypeMappings[searchItem.Resource.InstanceType];
                long resourceSurrogateId = searchItem.SurrogateId;
                string content = ((StringSearchValue)searchItem.SearchIndexEntry.Value).String;
                string overflow;
                string indexedPrefix;
                if (content.Length > 256)
                {
                    indexedPrefix = content.Substring(0, 256);
                    overflow = content;
                }
                else
                {
                    indexedPrefix = content;
                    overflow = null;
                }

                DataRow row = importTable.NewRow();
                row["ResourceTypeId"] = resourceTypeId;
                row["ResourceSurrogateId"] = resourceSurrogateId;
                row["SearchParamId"] = 1;
                row["Text"] = indexedPrefix;
                row["TextOverflow"] = overflow;
                row["IsHistory"] = false;
                importTable.Rows.Add(row);
            }

            using IDataReader reader = importTable.CreateDataReader();
            using (SqlBulkCopy bulkCopy =
                        new SqlBulkCopy(destinationConnection))
            {
                try
                {
                    bulkCopy.DestinationTableName =
                        "dbo.StringSearchParam";
                    await bulkCopy.WriteToServerAsync(reader);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
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
            column.DataType = typeof(long);
            column.ColumnName = "ResourceSurrogateId";
            column.ReadOnly = true;
            table.Columns.Add(column);

            column = new DataColumn();
            column.DataType = typeof(short);
            column.ColumnName = "SearchParamId";
            column.ReadOnly = true;
            table.Columns.Add(column);

            column = new DataColumn();
            column.DataType = typeof(string);
            column.ColumnName = "Text";
            column.ReadOnly = true;
            table.Columns.Add(column);

            column = new DataColumn();
            column.DataType = typeof(string);
            column.ColumnName = "TextOverflow";
            column.ReadOnly = true;
            table.Columns.Add(column);

            column = new DataColumn();
            column.DataType = typeof(bool);
            column.ColumnName = "IsHistory";
            column.ReadOnly = true;
            table.Columns.Add(column);

            return table;
        }
    }
}
