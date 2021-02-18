// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public class ImportResourceTableStep : IStep
    {
        private static readonly Encoding ResourceEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        private const int BatchSize = 1000;

        private long copiedCount = 0;
        private Channel<BulkCopyResourceWrapper> _input;
        private Task _runningTask;
        private IRawResourceFactory _rawResourceFactory;
        private ResourceIdProvider _resourceIdProvider;
        private IConfiguration _configuration;
        private Dictionary<string, short> _resourceTypeMappings;
        private List<BulkCopyResourceWrapper> _buffer = new List<BulkCopyResourceWrapper>();

        public ImportResourceTableStep(
            Channel<BulkCopyResourceWrapper> input,
            IRawResourceFactory rawResourceFactory,
            ResourceIdProvider resourceIdProvider,
            Dictionary<string, short> resourceTypeMappings,
            IConfiguration configuration)
        {
            _input = input;
            _rawResourceFactory = rawResourceFactory;
            _resourceIdProvider = resourceIdProvider;
            _configuration = configuration;
            _resourceTypeMappings = resourceTypeMappings;
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
                        await foreach (BulkCopyResourceWrapper resource in _input.Reader.ReadAllAsync())
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
            foreach (var resourceElement in data)
            {
                var rawDataString = GetRawDataString(resourceElement.Resource, true);
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
                row["RawResource"] = WriteCompressedRawResource(rawDataString);
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

                    copiedCount += importTable.Rows.Count;
                    Console.WriteLine($"{copiedCount} resource to table.");
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

        public string GetRawDataString(ResourceElement resource, bool keepMeta)
        {
            RawResource rawResource = _rawResourceFactory.Create(resource, keepMeta);
            return rawResource.Data;
        }

        public static byte[] WriteCompressedRawResource(string rawResource)
        {
            using var stream = new MemoryStream();

            using var gzipStream = new GZipStream(stream, CompressionMode.Compress, leaveOpen: true);
            using var writer = new StreamWriter(gzipStream, ResourceEncoding);
            writer.Write(rawResource);
            writer.Flush();

            stream.Seek(0, 0);

            return stream.ToArray();
        }
    }
}
