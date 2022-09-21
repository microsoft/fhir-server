// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.PostgresQL.TypeGenerators;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Import;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.JobManagement;
using Npgsql;
using static Microsoft.Health.Fhir.PostgresQL.TypeConvert;
using BulkTokenTextTableTypeV1Row = Microsoft.Health.Fhir.PostgresQL.TypeConvert.BulkTokenTextTableTypeV1Row;

namespace Microsoft.Health.Fhir.PostgresQL.Import
{
    public class PostgresQLImportOperation : ISqlImportOperation, IImportOrchestratorJobDataStoreOperation
    {
        private ILogger<PostgresQLImportOperation> _logger;
        private readonly ISqlServerFhirModel _model;
        private readonly TokenTextSearchParamsGenerator _tokenTextSearchParamsGenerator;

        public PostgresQLImportOperation(ILogger<PostgresQLImportOperation> logger, ISqlServerFhirModel model)
        {
            _logger = logger;
            _model = model;
            _tokenTextSearchParamsGenerator = new TokenTextSearchParamsGenerator(_model);
        }

        public async Task PreprocessAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(0, cancellationToken);
            return;
        }

        public async Task PostprocessAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(0, cancellationToken);
            return;
        }

        private static byte[] StreamToBytes(Stream stream)
        {
            byte[] bytes = new byte[stream.Length];
            stream.Read(bytes, 0, bytes.Length);
            stream.Seek(0, SeekOrigin.Begin);
            return bytes;
        }

        private static BulkImportResourceType ResourceTypeConvert(BulkImportResourceTypeV1Row resource)
        {
            return new BulkImportResourceType()
            {
                resourcetypeid = resource.ResourceTypeId,
                resourceid = resource.ResourceId,
                version = resource.Version,
                ishistory = resource.IsHistory,
                resourcesurrogateid = resource.ResourceSurrogateId,
                isdeleted = resource.IsDeleted,
                requestmethod = resource.RequestMethod,
                rawresource = StreamToBytes(resource.RawResource),
                israwresourcemetaset = resource.IsRawResourceMetaSet,
                searchparamhash = resource.SearchParamHash,
            };
        }

        public async Task<IEnumerable<SqlBulkCopyDataWrapper>> BulkMergeResourceAsync(IEnumerable<SqlBulkCopyDataWrapper> resources, CancellationToken cancellationToken)
        {
            try
            {
                var importedSurrogatedId = new List<long>();

                // Make sure there's no dup in this batch
                resources = resources.GroupBy(r => (r.ResourceTypeId, r.Resource?.ResourceId)).Select(r => r.First());
                IEnumerable<BulkImportResourceType> inputResources = resources.Select(r => ResourceTypeConvert(r.BulkImportResource));

                using (var conn = new NpgsqlConnection(PostgresQLConfiguration.DefaultConnectionString))
                {
                    try
                    {
                        await conn.OpenAsync(cancellationToken);
                        conn.TypeMapper.MapComposite<BulkImportResourceType>("bulkimportresourcetype_1");

                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = $"select bulkmergeresource_1((@resources))";
                            cmd.Parameters.Add(new NpgsqlParameter()
                            {
                                ParameterName = "resources",
                                Value = inputResources.ToList(),
                            });

                            await cmd.ExecuteNonQueryAsync(cancellationToken);

                            return resources;
                        }
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                    finally
                    {
                        await conn.CloseAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "BulkMergeResourceAsync failed.");
                throw new RetriableJobException(ex.Message, ex);
            }
        }

        public async Task CleanBatchResourceAsync(string resourceType, long beginSequenceId, long endSequenceId, CancellationToken cancellationToken)
        {
            await Task.Delay(0, cancellationToken);
            return;
        }

        public async Task BulkCopyDataAsync(IEnumerable<SqlBulkCopyDataWrapper> resources, CancellationToken cancellationToken)
        {
            try
            {
                resources = resources.GroupBy(r => (r.ResourceTypeId, r.Resource?.ResourceId)).Select(r => r.First());

                using (var conn = new NpgsqlConnection(PostgresQLConfiguration.DefaultConnectionString))
                {
                    try
                    {
                        await conn.OpenAsync(cancellationToken);
                        conn.TypeMapper.MapComposite<BulkImportResourceType>("bulkimportresourcetype_1");
                        conn.TypeMapper.MapComposite<BulkTokenTextTableTypeV1Row>("bulktokentexttabletype_2");

                        foreach (var resource in resources)
                        {
                            using (var cmd = conn.CreateCommand())
                            {
                                cmd.CommandText = $"select bulkmergetokentext((@resource), (@tokentexts))";
                                cmd.Parameters.Add(new NpgsqlParameter()
                                {
                                    ParameterName = "resource",
                                    Value = ResourceTypeConvert(resource.BulkImportResource),
                                });
                                cmd.Parameters.Add(new NpgsqlParameter()
                                {
                                    ParameterName = "tokentexts",
                                    Value = _tokenTextSearchParamsGenerator.GenerateRows(new List<ResourceWrapper>() { resource.Resource }).ToList(),
                                });

                                await cmd.ExecuteNonQueryAsync(cancellationToken);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                    finally
                    {
                        await conn.CloseAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "BulkMergeResourceAsync failed.");
                throw new RetriableJobException(ex.Message, ex);
            }
        }

        public async Task BulkCopyDataAsync(DataTable dataTable, CancellationToken cancellationToken)
        {
            if (!dataTable.TableName.Equals("TokenText", StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(0, cancellationToken);

            // await BulkCopyDataAsync(resources, cancellationToken);
        }
    }
}
