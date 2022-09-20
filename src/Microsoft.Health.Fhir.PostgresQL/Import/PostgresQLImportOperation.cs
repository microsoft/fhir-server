// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Import;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.JobManagement;
using Npgsql;
using static Microsoft.Health.Fhir.PostgresQL.TypeConvert;

namespace Microsoft.Health.Fhir.PostgresQL.Import
{
    public class PostgresQLImportOperation : ISqlImportOperation, IImportOrchestratorJobDataStoreOperation
    {
        private ILogger<PostgresQLImportOperation> _logger;

        public PostgresQLImportOperation(ILogger<PostgresQLImportOperation> logger)
        {
            _logger = logger;
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
                rawresource = resource.RawResource,
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
                            cmd.CommandText = $"select * from readresource((@resources))";
                            cmd.Parameters.Add(new NpgsqlParameter()
                            {
                                ParameterName = "resources",
                                Value = inputResources.ToList(),
                            });

                            var reader = await cmd.ExecuteReaderAsync(cancellationToken);

                            while (await reader.ReadAsync(cancellationToken))
                            {
                                importedSurrogatedId.Add(reader.GetInt64(0));
                            }

                            return resources.Where(r => importedSurrogatedId.Contains(r.ResourceSurrogateId));
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

        public async Task BulkCopyDataAsync(DataTable dataTable, CancellationToken cancellationToken)
        {
            await Task.Delay(0, cancellationToken);
            return;
        }
    }
}
