// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.IO.Compression;
using System.Text;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.PostgresQL.TypeGenerators;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.IO;
using Npgsql;
using NpgsqlTypes;
using static Microsoft.Health.Fhir.PostgresQL.TypeConvert;

namespace Microsoft.Health.Fhir.PostgresQL
{
    public class PostgresQLFhirDataStore : IFhirDataStore
    {
        internal static readonly Encoding LegacyResourceEncoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);
        internal static readonly Encoding ResourceEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        private const string ConnectionString = "";
        private readonly ISqlServerFhirModel _model;
        private readonly SchemaInformation _schemaInformation;
        private readonly ResourceWriteClaimsGenerator _resourceWriteClaimsGenerator;
        private readonly TokenTextSearchParamsGenerator _tokenTextSearchParamsGenerator;
        private readonly ILogger<PostgresQLFhirDataStore> _logger;
        private readonly RecyclableMemoryStreamManager _memoryStreamManager;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly CoreFeatureConfiguration _coreFeatures;

        public PostgresQLFhirDataStore(
            ISqlServerFhirModel model,
            SchemaInformation schemaInformation,
            ILogger<PostgresQLFhirDataStore> logger,
            IOptions<CoreFeatureConfiguration> coreFeatures,
            IModelInfoProvider modelInfoProvider)
        {
            _model = EnsureArg.IsNotNull(model, nameof(model));
            _schemaInformation = EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            _resourceWriteClaimsGenerator = new ResourceWriteClaimsGenerator(_model);
            _tokenTextSearchParamsGenerator = new TokenTextSearchParamsGenerator(_model);

            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _memoryStreamManager = new RecyclableMemoryStreamManager();
            _modelInfoProvider = EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            _coreFeatures = EnsureArg.IsNotNull(coreFeatures?.Value, nameof(coreFeatures));
        }

        public async Task<UpsertOutcome?> UpsertAsync(
            ResourceWrapper resource,
            WeakETag weakETag,
            bool allowCreate,
            bool keepHistory,
            CancellationToken cancellationToken,
            bool requireETagOnUpdate = false)
        {
            int? eTag = weakETag == null
                ? null
                : (int.TryParse(weakETag.VersionId, out var parsedETag) ? parsedETag : -1); // Set the etag to a sentinel value to enable expected failure paths when updating with both existing and nonexistent resources.

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (var stream = new RecyclableMemoryStream(_memoryStreamManager))
                {
                    // Read the latest resource
                    var existingResource = await GetAsync(new ResourceKey(resource.ResourceTypeName, resource.ResourceId), cancellationToken);

                    // Check for any validation errors
                    if (existingResource != null && eTag.HasValue && eTag.ToString() != existingResource.Version)
                    {
                        if (weakETag != null)
                        {
                            // The backwards compatibility behavior of Stu3 is to return 409 Conflict instead of a 412 Precondition Failed
                            if (_modelInfoProvider.Version == FhirSpecification.Stu3)
                            {
                                throw new ResourceConflictException(weakETag);
                            }

                            throw new PreconditionFailedException(string.Format(weakETag.VersionId));
                        }
                    }

                    int? existingVersion = null;

                    // There is no previous version of this resource, check validations and then simply call SP to create new version
                    if (existingResource == null)
                    {
                        if (resource.IsDeleted)
                        {
                            // Don't bother marking the resource as deleted since it already does not exist.
                            return null;
                        }

                        if (eTag.HasValue && eTag != null)
                        {
                            // You can't update a resource with a specified version if the resource does not exist
                            if (weakETag != null)
                            {
                                throw new ResourceNotFoundException(string.Format(resource.ResourceTypeName, resource.ResourceId, weakETag.VersionId));
                            }
                        }

                        if (!allowCreate)
                        {
                            throw new MethodNotAllowedException("ResourceCreationNotAllowed");
                        }
                    }
                    else
                    {
                        if (requireETagOnUpdate && !eTag.HasValue)
                        {
                            // This is a versioned update and no version was specified
                            // TODO: Add this to SQL error codes in AB#88286
                            // The backwards compatibility behavior of Stu3 is to return 412 Precondition Failed instead of a 400 Bad Request
                            if (_modelInfoProvider.Version == FhirSpecification.Stu3)
                            {
                                throw new PreconditionFailedException(string.Format(resource.ResourceTypeName));
                            }

                            throw new BadRequestException(string.Format(resource.ResourceTypeName));
                        }

                        if (resource.IsDeleted && existingResource.IsDeleted)
                        {
                            // Already deleted - don't create a new version
                            return null;
                        }

                        // check if reosurces are equal if its not a Delete action
                        if (!resource.IsDeleted)
                        {
                            // check if the new resource data is same as existing resource data
                            if (string.Equals(RemoveVersionIdAndLastUpdatedFromMeta(existingResource), RemoveVersionIdAndLastUpdatedFromMeta(resource), StringComparison.Ordinal))
                            {
                                // Send the existing resource in the response
                                return new UpsertOutcome(existingResource, SaveOutcomeType.Updated);
                            }
                        }

                        // existing version in the SQL db should never be null
                        existingVersion = int.Parse(existingResource.Version);
                        resource.Version = (existingVersion + 1).Value.ToString(CultureInfo.InvariantCulture);
                    }

                    WriteCompressedRawResource(stream, resource.RawResource.Data);

                    stream.Seek(0, 0);

                    _logger.LogInformation("Upserting {ResourceTypeName} with a stream length of {StreamLength}", resource.ResourceTypeName, stream.Length);

                    // throwing ServiceUnavailableException in order to send a 503 error message to the client
                    // indicating the server has a transient error, and the client can try again
                    if (stream.Length < 31) // rather than error on a length of 0, a stream with a small number of bytes should still throw an error. RawResource.Data = null, still results in a stream of 29 bytes
                    {
                        _logger.LogCritical("Stream size for resource of type: {ResourceTypeName} is less than 50 bytes, request method: {RequestMethod}", resource.ResourceTypeName, resource.Request.Method);
                        throw new ServiceUnavailableException();
                    }

                    PopulateUpsertResourceCommand(resource, allowCreate, keepHistory, requireETagOnUpdate, eTag, existingVersion, stream, _coreFeatures.SupportsResourceChangeCapture);

                    try
                    {
                        // TODO
                        return new UpsertOutcome(resource, SaveOutcomeType.Updated);
                    }
                    catch (NpgsqlException)
                    {
                        throw;
                    }
                }
            }
        }

        public static void WriteCompressedRawResource(Stream outputStream, string rawResource)
        {
            using var gzipStream = new GZipStream(outputStream, CompressionMode.Compress, leaveOpen: true);
            using var writer = new StreamWriter(gzipStream, ResourceEncoding);
            writer.Write(rawResource);
        }

        private void PopulateUpsertResourceCommand(
            ResourceWrapper resource,
            bool allowCreate,
            bool keepHistory,
            bool requireETagOnUpdate,
            int? eTag,
            int? comparedVersion,
            RecyclableMemoryStream stream,
            bool isResourceChangeCaptureEnabled)
        {
            long baseResourceSurrogateId = ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(resource.LastModified.UtcDateTime);
            short resourceTypeId = 103;

            if (_schemaInformation.Current >= SchemaVersionConstants.PreventUpdatesFromCreatingVersionWhenNoImpact)
            {
                try
                {
                    using (var conn = new NpgsqlConnection(ConnectionString))
                    {
                        conn.Open();
                        conn.TypeMapper.MapComposite<BulkResourceWriteClaimTableTypeV1Row>("bulkresourcewriteclaimtabletype_1");
                        conn.TypeMapper.MapComposite<BulkTokenTextTableTypeV1Row>("bulktokentexttabletype_2");
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = $"call upsertresource_3((@baseresourcesurrogateid)," +
                                $"(@restypeid)," +
                                $"(@resid), " +
                                $"(@etag), " +
                                $"(@allowcreate), " +
                                $"(@isdeleted), " +
                                $"(@keephistory), " +
                                $"(@requireetagonupdate), " +
                                $"(@requestmethod), " +
                                $"(@searchparamhash), " +
                                $"(@rawresource), " +
                                $"(@resourcewriteclaims), " +
                                $"(@tokentextsearchparams), " +
                                $"(@isresourcechangecaptureenabled), " +
                                $"(@comparedversion))";
                            cmd.Parameters.Add(new NpgsqlParameter("baseresourcesurrogateid", NpgsqlDbType.Bigint) { Value = baseResourceSurrogateId });
                            cmd.Parameters.Add(new NpgsqlParameter("restypeid", NpgsqlDbType.Smallint) { Value = resourceTypeId });
                            cmd.Parameters.Add(new NpgsqlParameter("resid", NpgsqlDbType.Varchar) { Value = resource.ResourceId });
                            cmd.Parameters.Add(new NpgsqlParameter("etag", NpgsqlDbType.Integer) { Value = eTag == null ? 0 : 1 });
                            cmd.Parameters.Add(new NpgsqlParameter("allowcreate", NpgsqlDbType.Bit) { Value = allowCreate });
                            cmd.Parameters.Add(new NpgsqlParameter("isdeleted", NpgsqlDbType.Bit) { Value = resource.IsDeleted });
                            cmd.Parameters.Add(new NpgsqlParameter("keephistory", NpgsqlDbType.Bit) { Value = keepHistory });
                            cmd.Parameters.Add(new NpgsqlParameter("requireetagonupdate", NpgsqlDbType.Bit) { Value = requireETagOnUpdate });
                            cmd.Parameters.Add(new NpgsqlParameter("requestmethod", NpgsqlDbType.Varchar) { Value = resource.Request.Method });
                            cmd.Parameters.Add(new NpgsqlParameter("searchparamhash", NpgsqlDbType.Varchar) { Value = resource.SearchParameterHash == null ? "test hash" : resource.SearchParameterHash });
                            cmd.Parameters.Add(new NpgsqlParameter("rawresource", NpgsqlDbType.Bytea) { Value = StreamToBytes(stream) });
                            cmd.Parameters.Add(new NpgsqlParameter
                            {
                                ParameterName = "resourcewriteclaims",
                                Value = _resourceWriteClaimsGenerator.GenerateRows(new List<ResourceWrapper> { resource }).ToList(),
                            });
                            cmd.Parameters.Add(new NpgsqlParameter
                            {
                                ParameterName = "tokentextsearchparams",
                                Value = _tokenTextSearchParamsGenerator.GenerateRows(new List<ResourceWrapper> { resource }).ToList(),
                            });
                            cmd.Parameters.Add(new NpgsqlParameter("isresourcechangecaptureenabled", NpgsqlDbType.Bit) { Value = isResourceChangeCaptureEnabled });
                            cmd.Parameters.Add(new NpgsqlParameter("comparedversion", NpgsqlDbType.Integer) { Value = comparedVersion == null ? 0 : comparedVersion });

                            cmd.ExecuteNonQuery();
                            conn.Close();
                        }
                    }
                }
                catch (Exception)
                {
                    throw;
                }
            }
            else
            {
                throw new InvalidOperationException("No support schema version");
            }
        }

        public static byte[] StreamToBytes(Stream stream)
        {
            byte[] bytes = new byte[stream.Length];
            stream.Read(bytes, 0, bytes.Length);
            stream.Seek(0, SeekOrigin.Begin);
            return bytes;
        }

        public async Task<ResourceWrapper?> GetAsync(ResourceKey key, CancellationToken cancellationToken)
        {
            int? requestedVersion = null;
            if (!string.IsNullOrEmpty(key.VersionId))
            {
                if (!int.TryParse(key.VersionId, out var parsedVersion))
                {
                    return null;
                }

                requestedVersion = parsedVersion;
            }

            try
            {
                using (var conn = new NpgsqlConnection(ConnectionString))
                {
                    await conn.OpenAsync(cancellationToken);
                    try
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = $"select * from readresource((@restypeid), (@resid), (@vers))";
                            cmd.Parameters.Add(new NpgsqlParameter("restypeid", NpgsqlDbType.Smallint) { Value = _model.GetResourceTypeId(key.ResourceType) });
                            cmd.Parameters.Add(new NpgsqlParameter("resid", NpgsqlDbType.Varchar) { Value = key.Id });
                            cmd.Parameters.Add(new NpgsqlParameter("vers", NpgsqlDbType.Integer) { Value = requestedVersion == null ? 1 : requestedVersion });
                            var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                            long resourceSurrogateId = 0;
                            int version = 0;
                            bool isDeleted = false;
                            bool isHistory = false;
                            bool isRawResourceMetaSet = false;
                            string rawResource = string.Empty;
                            string? searchParamHash = null;

                            while (await reader.ReadAsync(cancellationToken))
                            {
                                resourceSurrogateId = reader.GetInt64(0);
                                version = reader.GetInt32(1);
                                isDeleted = reader.GetBoolean(2);
                                isHistory = reader.GetBoolean(3);
                                using Stream rawResourceStream = reader.GetStream(4);
                                rawResource = ReadCompressedRawResource(rawResourceStream);
                                isRawResourceMetaSet = reader.GetBoolean(5);
                                searchParamHash = reader.GetString(6);
                            }

                            if (string.IsNullOrEmpty(rawResource))
                            {
                                return null;
                            }

                            return new ResourceWrapper(
                                key.Id,
                                version.ToString(CultureInfo.InvariantCulture),
                                key.ResourceType,
                                new RawResource(rawResource, FhirResourceFormat.Json, isMetaSet: isRawResourceMetaSet),
                                null,
                                new DateTimeOffset(ResourceSurrogateIdToLastUpdated(resourceSurrogateId), TimeSpan.Zero),
                                isDeleted,
                                searchIndices: null,
                                compartmentIndices: null,
                                lastModifiedClaims: null,
                                searchParamHash)
                            {
                                IsHistory = isHistory,
                            };
                        }
                    }
                    finally
                    {
                        await conn.CloseAsync();
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        private static string RemoveTrailingZerosFromMillisecondsForAGivenDate(DateTimeOffset date)
        {
            // 0000000+ -> +, 0010000+ -> 001+, 0100000+ -> 01+, 0180000+ -> 018+, 1000000 -> 1+, 1100000+ -> 11+, 1010000+ -> 101+
            // ToString("o") - Formats to 2022-03-09T01:40:52.0690000+02:00 but serialized value to string in dB is 2022-03-09T01:40:52.069+02:00
            var formattedDate = date.ToString("o", CultureInfo.InvariantCulture);
            var milliseconds = formattedDate.Substring(20, 7); // get 0690000
            var trimmedMilliseconds = milliseconds.TrimEnd('0'); // get 069
            if (milliseconds.Equals("0000000", StringComparison.Ordinal))
            {
                // when date = 2022-03-09T01:40:52.0000000+02:00, value in dB is 2022-03-09T01:40:52+02:00, we need to replace the . after second
                return formattedDate.Replace("." + milliseconds, string.Empty, StringComparison.Ordinal);
            }

            return formattedDate.Replace(milliseconds, trimmedMilliseconds, StringComparison.Ordinal);
        }

        private static string RemoveVersionIdAndLastUpdatedFromMeta(ResourceWrapper resourceWrapper)
        {
            var versionToReplace = resourceWrapper.RawResource.IsMetaSet ? resourceWrapper.Version : "1";
            var rawResource = resourceWrapper.RawResource.Data.Replace($"\"versionId\":\"{versionToReplace}\"", string.Empty, StringComparison.Ordinal);
            return rawResource.Replace($"\"lastUpdated\":\"{RemoveTrailingZerosFromMillisecondsForAGivenDate(resourceWrapper.LastModified)}\"", string.Empty, StringComparison.Ordinal);
        }

        public static DateTime ResourceSurrogateIdToLastUpdated(long resourceSurrogateId)
        {
            var dateTime = new DateTime(resourceSurrogateId >> 3, DateTimeKind.Utc);
            return dateTime.TruncateToMillisecond();
        }

        private static string ReadCompressedRawResource(Stream compressedResourceStream)
        {
            using var gzipStream = new GZipStream(compressedResourceStream, CompressionMode.Decompress, leaveOpen: true);

            // The current resource encoding uses byte-order marks. The legacy encoding does not, so we provide is as the fallback encoding
            // when there is no BOM
            using var reader = new StreamReader(gzipStream, LegacyResourceEncoding, detectEncodingFromByteOrderMarks: true);

            // The synchronous method is being used as it was found to be ~10x faster than the asynchronous method.
            return reader.ReadToEnd();
        }

        public Task HardDeleteAsync(ResourceKey key, bool keepCurrentVersion, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task BulkUpdateSearchParameterIndicesAsync(IReadOnlyCollection<ResourceWrapper> resources, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<ResourceWrapper> UpdateSearchParameterIndicesAsync(ResourceWrapper resourceWrapper, WeakETag weakETag, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<int?> GetProvisionedDataStoreCapacityAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
