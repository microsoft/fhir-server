// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Configs;
using Microsoft.IO;
using Microsoft.SqlServer.Server;
using Newtonsoft.Json;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.SqlServer
{
    public class SqlServerDataStore : IDataStore, IProvideCapability
    {
        private readonly SqlServerDataStoreConfiguration _configuration;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private Dictionary<string, short> _resourceTypeToId;
        private Dictionary<short, string> _resourceTypeIdToTypeName;
        private Dictionary<(string, byte?), short> _searchParamUrlToId;
        private readonly RecyclableMemoryStreamManager _memoryStreamManager;
        private static readonly SqlMetaData[] StringSearchParamTableValuedParameterColumns = { new SqlMetaData("ResourceTypePK", SqlDbType.SmallInt), new SqlMetaData("SearchParamPK", SqlDbType.SmallInt), new SqlMetaData("CompositeCorrelationId", SqlDbType.TinyInt), new SqlMetaData("Value", SqlDbType.NVarChar, 512) };
        private static readonly SqlMetaData[] DateSearchParamTableValuedParameterColumns = { new SqlMetaData("ResourceTypePK", SqlDbType.SmallInt), new SqlMetaData("SearchParamPK", SqlDbType.SmallInt), new SqlMetaData("CompositeCorrelationId", SqlDbType.TinyInt), new SqlMetaData("StartTime", SqlDbType.DateTime2), new SqlMetaData("EndTime", SqlDbType.DateTime2) };
        private static readonly SqlMetaData[] ReferenceSearchParamTableValuedParameterColumns = { new SqlMetaData("ResourceTypePK", SqlDbType.SmallInt), new SqlMetaData("SearchParamPK", SqlDbType.SmallInt), new SqlMetaData("CompositeCorrelationId", SqlDbType.TinyInt), new SqlMetaData("BaseUri", SqlDbType.VarChar, 512), new SqlMetaData("ReferenceResourceTypePK", SqlDbType.SmallInt), new SqlMetaData("ReferenceResourceId", SqlDbType.VarChar, 64) };
        private static readonly SqlMetaData[] TokenSearchParamTableValuedParameterColumns = { new SqlMetaData("ResourceTypePK", SqlDbType.SmallInt), new SqlMetaData("SearchParamPK", SqlDbType.SmallInt), new SqlMetaData("CompositeCorrelationId", SqlDbType.TinyInt), new SqlMetaData("System", SqlDbType.NVarChar, 256), new SqlMetaData("Code", SqlDbType.NVarChar, 256), new SqlMetaData("TextHash", SqlDbType.Binary, 32) };
        private static readonly SqlMetaData[] QuantitySearchParamTableValuedParameterColumns = { new SqlMetaData("ResourceTypePK", SqlDbType.SmallInt), new SqlMetaData("SearchParamPK", SqlDbType.SmallInt), new SqlMetaData("CompositeCorrelationId", SqlDbType.TinyInt), new SqlMetaData("System", SqlDbType.NVarChar, 256), new SqlMetaData("Code", SqlDbType.NVarChar, 256), new SqlMetaData("Quantity", SqlDbType.Decimal, 18, 6) };
        private static readonly SqlMetaData[] NumberSearchParamTableValuedParameterColumns = { new SqlMetaData("ResourceTypePK", SqlDbType.SmallInt), new SqlMetaData("SearchParamPK", SqlDbType.SmallInt), new SqlMetaData("CompositeCorrelationId", SqlDbType.TinyInt), new SqlMetaData("Number", SqlDbType.Decimal, 18, 6) };
        private static readonly SqlMetaData[] UriSearchParamTableValuedParameterColumns = { new SqlMetaData("ResourceTypePK", SqlDbType.SmallInt), new SqlMetaData("SearchParamPK", SqlDbType.SmallInt), new SqlMetaData("CompositeCorrelationId", SqlDbType.TinyInt), new SqlMetaData("Uri", SqlDbType.VarChar, 256) };
        private static readonly SqlMetaData[] TextTableValuedParameterColumns = { new SqlMetaData("Hash", SqlDbType.Binary, 32), new SqlMetaData("Text", SqlDbType.NVarChar, 512) };
        private static readonly SqlMetaData[] UriTableValuedParameterColumns = { new SqlMetaData("Uri", SqlDbType.VarChar, 512) };

        [ThreadStatic]
        private static SHA256 s_sha256;

        public SqlServerDataStore(SqlServerDataStoreConfiguration configuration, ISearchParameterDefinitionManager searchParameterDefinitionManager)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));

            _configuration = configuration;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _memoryStreamManager = new RecyclableMemoryStreamManager();

            InitializeStore().GetAwaiter().GetResult();
        }

        private static SHA256 Sha256 => s_sha256 ?? (s_sha256 = SHA256.Create());

        private async Task InitializeStore()
        {
            using (var connection = new SqlConnection(_configuration.ConnectionString))
            {
                await connection.OpenAsync();
                using (SqlCommand sqlCommand = connection.CreateCommand())
                {
                    sqlCommand.CommandText =
                        @"INSERT INTO dbo.ResourceType (Name) 
                          SELECT value FROM string_split(@p1, ',')
                          EXCEPT SELECT Name from dbo.ResourceType; 
                          
                          SELECT ResourceTypePK, Name FROM dbo.ResourceType;
                
                          INSERT INTO dbo.SearchParam 
                              ([Name], [Uri], [ComponentIndex])
                          SELECT * FROM  OPENJSON (@p2) 
                          WITH ([Name] varchar(200) '$.Name', [Uri] varchar(128) '$.Uri', [ComponentIndex] tinyint '$.ComponentIndex')
                          EXCEPT SELECT Name, Uri, ComponentIndex from dbo.SearchParam;
                
                          SELECT SearchParamPK, Uri, ComponentIndex FROM dbo.SearchParam;";

                    sqlCommand.Parameters.AddWithValue("@p1", string.Join(",", ModelInfo.SupportedResources));
                    sqlCommand.Parameters.AddWithValue("@p2", JsonConvert.SerializeObject(GetSearchParameterDefinitions()));

                    using (SqlDataReader reader = await sqlCommand.ExecuteReaderAsync())
                    {
                        _resourceTypeToId = new Dictionary<string, short>(StringComparer.Ordinal);
                        _resourceTypeIdToTypeName = new Dictionary<short, string>();
                        while (await reader.ReadAsync())
                        {
                            string resourceTypeName = reader.GetString(1);
                            short id = reader.GetInt16(0);
                            _resourceTypeToId.Add(resourceTypeName, id);
                            _resourceTypeIdToTypeName.Add(id, resourceTypeName);
                        }

                        await reader.NextResultAsync();

                        _searchParamUrlToId = new Dictionary<(string, byte?), short>();
                        while (await reader.ReadAsync())
                        {
                            _searchParamUrlToId.Add(
                                (reader.GetString(1), reader.IsDBNull(2) ? (byte?)null : reader.GetByte(2)),
                                reader.GetInt16(0));
                        }
                    }
                }
            }
        }

        private IEnumerable<dynamic> GetSearchParameterDefinitions()
        {
            foreach (SearchParameter p in _searchParameterDefinitionManager.AllSearchParameters)
            {
                if (p.Type == SearchParamType.Composite)
                {
                    for (int i = 0; i < p.Component.Count; i++)
                    {
                        yield return new { p.Name, Uri = p.Url, ComponentIndex = (int?)i };
                    }
                }
                else
                {
                    yield return new { p.Name, Uri = p.Url, ComponentIndex = (int?)null };
                }
            }
        }

        public async Task<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken cancellationToken = default)
        {
            using (var connection = new SqlConnection(_configuration.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken);

                using (SqlCommand command = connection.CreateCommand())
                {
                    bool versionedRead = !string.IsNullOrEmpty(key.VersionId);

                    if (versionedRead)
                    {
                        command.CommandText = $@"
SELECT Version, LastUpdated, RawResource 
FROM dbo.Resource
WHERE ResourceTypePK = @resourceTypePK AND Id = @id 
AND Version = @version";
                    }
                    else
                    {
                        command.CommandText = $@"
SELECT TOP 1 Version, LastUpdated, RawResource
FROM dbo.Resource
WHERE ResourceTypePK = @resourceTypePK AND Id = @id 
ORDER BY [Version] DESC";
                    }

                    command.Parameters.AddWithValue("@resourceTypePK", _resourceTypeToId[key.ResourceType]);
                    command.Parameters.AddWithValue("@id", key.Id);
                    if (versionedRead)
                    {
                        if (!int.TryParse(key.VersionId, out var version))
                        {
                            return null;
                        }

                        command.Parameters.AddWithValue("@version", version);
                    }

                    using (SqlDataReader sqlDataReader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                    {
                        if (!sqlDataReader.Read())
                        {
                            return null;
                        }

                        int version = sqlDataReader.GetInt32(0);
                        DateTime lastModified = sqlDataReader.GetDateTime(1);
                        string rawResource;

                        using (var dataStream = sqlDataReader.GetStream(2))
                        using (var ms = new RecyclableMemoryStream(_memoryStreamManager))
                        using (var gzipStream = new GZipStream(dataStream, CompressionMode.Decompress, true))
                        {
                            await gzipStream.CopyToAsync(ms, cancellationToken);
                            rawResource = Encoding.UTF8.GetString(ms.GetBuffer().AsSpan().Slice(0, (int)ms.Length));
                        }

                        return new ResourceWrapper(
                            key.Id,
                            version.ToString(CultureInfo.InvariantCulture),
                            key.ResourceType,
                            new RawResource(rawResource, ResourceFormat.Json),
                            new ResourceRequest("https://exmaple", HttpMethod.Get),
                            new DateTimeOffset(lastModified, TimeSpan.Zero),
                            false,
                            ImmutableArray<KeyValuePair<string, string>>.Empty);
                    }
                }
            }
        }

        public Task HardDeleteAsync(ResourceKey key, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public async Task<UpsertOutcome> UpsertAsync(ResourceWrapper resource, WeakETag weakETag, bool isCreate, bool allowCreate, bool keepHistory, CancellationToken cancellationToken = default)
        {
            using (var connection = new SqlConnection(_configuration.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken);

                IReadOnlyCollection<SearchIndexEntry> searchIndexEntries = ((ISupportSearchIndices)resource).SearchIndices;
                ILookup<Type, (SearchParameter searchParameter, byte? componentIndex, byte? compositeCorrelationId, ISearchValue value)> lookupByType = GroupSearchIndexEntriesByType(searchIndexEntries);

                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        $@"
SET XACT_ABORT ON
BEGIN TRANSACTION

DECLARE @resourcePk bigint
DECLARE @version int = {(isCreate ? "1" : "(SELECT MAX(Version) FROM dbo.Resource WHERE ResourceTypePK = @resourceTypePK AND Id = @id) + 1")}

IF @version IS NULL 
    SET @version = 1

INSERT INTO dbo.Resource 
(ResourceTypePK, Id, Version, LastUpdated, RawResource)
VALUES (@resourceTypePK, @id, @version, SYSUTCDATETIME(), @rawResource)
SET @resourcePK = SCOPE_IDENTITY();

INSERT INTO dbo.StringSearchParam
(ResourceTypePK, ResourcePK, SearchParamPK, CompositeCorrelationId, Value)
SELECT ResourceTypePK, @resourcePK, SearchParamPK, CompositeCorrelationId, Value FROM @tvpStringSearchParam

INSERT INTO dbo.TokenText (Hash, Text)
SELECT Hash, Text 
FROM @tvpTokenText p
WHERE NOT EXISTS (SELECT 1 FROM dbo.TokenText where [Hash] = p.Hash)

INSERT INTO dbo.TokenSearchParam
(ResourceTypePK, ResourcePK, SearchParamPK, CompositeCorrelationId, System, Code, TextHash)
SELECT ResourceTypePK, @resourcePK, SearchParamPK, CompositeCorrelationId, System, Code, TextHash FROM @tvpTokenSearchParam

INSERT INTO dbo.DateSearchParam
(ResourceTypePK, ResourcePK, SearchParamPK, CompositeCorrelationId, StartTime, EndTime)
SELECT ResourceTypePK, @resourcePK, SearchParamPK, CompositeCorrelationId, StartTime, EndTime FROM @tvpDateSearchParam

DECLARE @dummy int
DECLARE @uriMerged TABLE ([UriPK] int, [Uri] varchar(512))

MERGE dbo.Uri AS t
USING @tvpUri AS s
ON t.[Uri] = s.[Uri]
WHEN NOT MATCHED BY TARGET THEN
	INSERT ([Uri]) VALUES ([Uri])
WHEN MATCHED THEN
    UPDATE SET @dummy = 1
OUTPUT inserted.* INTO @uriMerged;

INSERT INTO dbo.ReferenceSearchParam
(ResourceTypePK, ResourcePK, SearchParamPK, CompositeCorrelationId, BaseUriPK, ReferenceResourceTypePK, ReferenceResourceId)
SELECT p.ResourceTypePK, @resourcePK, p.SearchParamPK, p.CompositeCorrelationId, m.UriPK, p.ReferenceResourceTypePK, p.ReferenceResourceId 
FROM @tvpReferenceSearchParam p
LEFT JOIN @uriMerged m 
ON p.[BaseUri] = m.[Uri]

INSERT INTO dbo.QuantitySearchParam
(ResourceTypePK, ResourcePK, SearchParamPK, CompositeCorrelationId, System, Code, Quantity)
SELECT ResourceTypePK, @resourcePK, SearchParamPK, CompositeCorrelationId, System, Code, Quantity FROM @tvpQuantitySearchParam

INSERT INTO dbo.NumberSearchParam
(ResourceTypePK, ResourcePK, SearchParamPK, CompositeCorrelationId, Number)
SELECT ResourceTypePK, @resourcePK, SearchParamPK, CompositeCorrelationId, Number FROM @tvpNumberSearchParam

INSERT INTO dbo.UriSearchParam
(ResourceTypePK, ResourcePK, SearchParamPK, CompositeCorrelationId, Uri)
SELECT ResourceTypePK, @resourcePK, SearchParamPK, CompositeCorrelationId, Uri FROM @tvpUriSearchParam

COMMIT TRANSACTION

select @version";

                    short resourceTypePk = _resourceTypeToId[resource.ResourceTypeName];
                    command.Parameters.AddWithValue("@resourceTypePK", resourceTypePk);
                    command.Parameters.Add(new SqlParameter("@id", SqlDbType.VarChar, 64) { Value = resource.ResourceId });

                    byte[] bytes = ArrayPool<byte>.Shared.Rent(resource.RawResource.Data.Length * 4);
                    try
                    {
                        using (var ms = new RecyclableMemoryStream(_memoryStreamManager))
                        {
                            using (var gzipStream = new GZipStream(ms, CompressionMode.Compress, true))
                            {
                                gzipStream.Write(bytes.AsSpan().Slice(0, Encoding.UTF8.GetBytes(resource.RawResource.Data.AsSpan(), bytes.AsSpan())));
                            }

                            ms.Seek(0, 0);

                            command.Parameters.AddWithValue("@rawResource", ms.GetBuffer()).Size = (int)ms.Length;

                            AddStringSearchParams(lookupByType, resourceTypePk, command);
                            AddTokenSearchParams(lookupByType, resourceTypePk, command);
                            AddDateSearchParams(lookupByType, resourceTypePk, command);

                            AddReferenceSearchParams(lookupByType, resourceTypePk, command);
                            AddQuantitySearchParams(lookupByType, resourceTypePk, command);
                            AddNumberSearchParams(lookupByType, resourceTypePk, command);
                            AddUriSearchParams(lookupByType, resourceTypePk, command);

                            resource.Version = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
                            return new UpsertOutcome(resource, SaveOutcomeType.Created);
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(bytes);
                    }
                }
            }
        }

        private static ILookup<Type, (SearchParameter searchParameter, byte? componentIndex, byte? compositeCorrelationId, ISearchValue value)> GroupSearchIndexEntriesByType(IReadOnlyCollection<SearchIndexEntry> searchIndexEntries)
        {
            IEnumerable<(SearchParameter searchParameter, byte? componentIndex, byte? compositeCorrelationId, ISearchValue value)> Flatten()
            {
                byte compositeCorrelationId = 0;
                foreach (var searchIndexEntry in searchIndexEntries)
                {
                    if (searchIndexEntry.Value is CompositeSearchValue composite)
                    {
                        for (byte index = 0; index < composite.Components.Count; index++)
                        {
                            foreach (ISearchValue componentValue in composite.Components[index])
                            {
                                yield return (searchIndexEntry.SearchParameter, index, compositeCorrelationId, componentValue);
                            }
                        }

                        compositeCorrelationId++;
                    }
                    else
                    {
                        yield return (searchIndexEntry.SearchParameter, null, null, searchIndexEntry.Value);
                    }
                }
            }

            return Flatten().ToLookup(e => e.value.GetType());
        }

        private void AddTokenSearchParams(ILookup<Type, (SearchParameter searchParameter, byte? componentIndex, byte? compositeCorrelationId, ISearchValue value)> lookupByType, short resourceTypePk, SqlCommand command)
        {
            var tokenEntries = lookupByType[typeof(TokenSearchValue)]
                .Where(e => !string.Equals(e.searchParameter.Name, SearchParameterNames.ResourceType, StringComparison.Ordinal) &&
                            !string.Equals(e.searchParameter.Name, SearchParameterNames.Id, StringComparison.Ordinal))
                .Select(e =>
                {
                    string text;
                    byte[] hash;
                    var tokenSearchValue = (TokenSearchValue)e.value;

                    if (string.IsNullOrWhiteSpace(tokenSearchValue.Text) || e.componentIndex != null)
                    {
                        // cannot perform text searches on composite params
                        text = null;
                        hash = null;
                    }
                    else
                    {
                        text = tokenSearchValue.Text.ToUpperInvariant();
                        byte[] bytes = ArrayPool<byte>.Shared.Rent(text.Length * 4);
                        try
                        {
                            int byteLength = Encoding.UTF8.GetBytes(text.AsSpan(), bytes.AsSpan());
                            hash = Sha256.ComputeHash(bytes, 0, byteLength);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(bytes);
                        }
                    }

                    return (e.searchParameter, e.componentIndex, e.compositeCorrelationId, tokenSearchValue.System, tokenSearchValue.Code, hash, text);
                })
                .ToList();

            SqlDataRecord[] tokenRecords = tokenEntries.Select(e =>
            {
                var r = new SqlDataRecord(TokenSearchParamTableValuedParameterColumns);
                r.SetInt16(0, resourceTypePk);
                r.SetInt16(1, _searchParamUrlToId[(e.searchParameter.Url, e.componentIndex)]);
                if (e.compositeCorrelationId != null)
                {
                    r.SetByte(2, e.compositeCorrelationId.Value);
                }

                if (!string.IsNullOrWhiteSpace(e.System))
                {
                    r.SetString(3, e.System);
                }

                if (!string.IsNullOrWhiteSpace(e.Code))
                {
                    r.SetString(4, e.Code);
                }

                if (e.hash != null)
                {
                    r.SetBytes(5, 0, e.hash, 0, e.hash.Length);
                }

                return r;
            }).ToArray();

            SqlParameter param = command.Parameters.AddWithValue("@tvpTokenSearchParam", tokenRecords.Length == 0 ? null : tokenRecords);
            param.SqlDbType = SqlDbType.Structured;
            param.TypeName = "dbo.TokenSearchParamTableType";

            SqlDataRecord[] textRecords = tokenEntries
                .Where(e => e.hash != null)
                .Select(e => (e.hash, e.text))
                .Distinct(Sha256HashEqualityComparer.Instance)
                .Select(e =>
                {
                    var r = new SqlDataRecord(TextTableValuedParameterColumns);
                    r.SetBytes(0, 0, e.hash, 0, e.hash.Length);
                    r.SetString(1, e.text);
                    return r;
                }).ToArray();

            SqlParameter tokenTextParam = command.Parameters.AddWithValue("@tvpTokenText", textRecords.Length == 0 ? null : textRecords);
            tokenTextParam.SqlDbType = SqlDbType.Structured;
            tokenTextParam.TypeName = "dbo.TokenTextTableType";
        }

        private void AddStringSearchParams(ILookup<Type, (SearchParameter searchParameter, byte? componentIndex, byte? compositeCorrelationId, ISearchValue value)> lookupByType, short resourceTypePk, SqlCommand command)
        {
            var stringEntries = lookupByType[typeof(StringSearchValue)].ToList();
            SqlDataRecord[] stringRecords = stringEntries.Select(e =>
            {
                var r = new SqlDataRecord(StringSearchParamTableValuedParameterColumns);
                r.SetInt16(0, resourceTypePk);
                r.SetInt16(1, _searchParamUrlToId[(e.searchParameter.Url, e.componentIndex)]);
                if (e.compositeCorrelationId != null)
                {
                    r.SetByte(2, e.compositeCorrelationId.Value);
                }

                r.SetString(3, ((StringSearchValue)e.value).String);
                return r;
            }).ToArray();

            SqlParameter param = command.Parameters.AddWithValue("@tvpStringSearchParam", stringRecords.Length == 0 ? null : stringRecords);
            param.SqlDbType = SqlDbType.Structured;
            param.TypeName = "dbo.StringSearchParamTableType";
        }

        private void AddDateSearchParams(ILookup<Type, (SearchParameter searchParameter, byte? componentIndex, byte? compositeCorrelationId, ISearchValue value)> lookupByType, short resourceTypePk, SqlCommand command)
        {
            var entries = lookupByType[typeof(DateTimeSearchValue)].ToList();

            SqlDataRecord[] records = entries.Select(e =>
            {
                var r = new SqlDataRecord(DateSearchParamTableValuedParameterColumns);
                r.SetInt16(0, resourceTypePk);
                r.SetInt16(1, _searchParamUrlToId[(e.searchParameter.Url, e.componentIndex)]);
                if (e.compositeCorrelationId != null)
                {
                    r.SetByte(2, e.compositeCorrelationId.Value);
                }

                r.SetDateTime(3, ((DateTimeSearchValue)e.value).Start.ToUniversalTime().DateTime);
                r.SetDateTime(4, ((DateTimeSearchValue)e.value).End.ToUniversalTime().DateTime);
                return r;
            }).ToArray();

            SqlParameter param = command.Parameters.AddWithValue("@tvpDateSearchParam", records.Length == 0 ? null : records);
            param.SqlDbType = SqlDbType.Structured;
            param.TypeName = "dbo.DateSearchParamTableType";
        }

        private void AddReferenceSearchParams(ILookup<Type, (SearchParameter searchParameter, byte? componentIndex, byte? compositeCorrelationId, ISearchValue value)> lookupByType, short resourceTypePk, SqlCommand command)
        {
            var entries = lookupByType[typeof(ReferenceSearchValue)].ToList();

            SqlDataRecord[] uriRecords = entries
                .Select(e => ((ReferenceSearchValue)e.value).BaseUri)
                .Where(u => u != null)
                .Select(u =>
                {
                    var r = new SqlDataRecord(UriTableValuedParameterColumns);
                    r.SetString(0, u.ToString());
                    return r;
                }).ToArray();

            SqlParameter uriParam = command.Parameters.AddWithValue("@tvpUri", uriRecords.Length == 0 ? null : uriRecords);
            uriParam.SqlDbType = SqlDbType.Structured;
            uriParam.TypeName = "dbo.UriTableType";

            SqlDataRecord[] referenceParamRecords = entries.Select(e =>
            {
                var referenceSearchValue = (ReferenceSearchValue)e.value;

                var r = new SqlDataRecord(ReferenceSearchParamTableValuedParameterColumns);
                r.SetInt16(0, resourceTypePk);
                r.SetInt16(1, _searchParamUrlToId[(e.searchParameter.Url, e.componentIndex)]);
                if (e.compositeCorrelationId != null)
                {
                    r.SetByte(2, e.compositeCorrelationId.Value);
                }

                if (referenceSearchValue.BaseUri != null)
                {
                    r.SetString(3, referenceSearchValue.BaseUri.ToString());
                }

                if (referenceSearchValue.ResourceType != null)
                {
                    r.SetInt16(4, _resourceTypeToId[referenceSearchValue.ResourceType.ToString()]);
                }

                r.SetString(5, referenceSearchValue.ResourceId);
                return r;
            }).ToArray();

            SqlParameter referenceParam = command.Parameters.AddWithValue("@tvpReferenceSearchParam", referenceParamRecords.Length == 0 ? null : referenceParamRecords);
            referenceParam.SqlDbType = SqlDbType.Structured;
            referenceParam.TypeName = "dbo.ReferenceSearchParamTableType";
        }

        private void AddQuantitySearchParams(ILookup<Type, (SearchParameter searchParameter, byte? componentIndex, byte? compositeCorrelationId, ISearchValue value)> lookupByType, short resourceTypePk, SqlCommand command)
        {
            var entries = lookupByType[typeof(QuantitySearchValue)].ToList();

            SqlDataRecord[] records = entries.Select(e =>
            {
                var value = (QuantitySearchValue)e.value;
                var r = new SqlDataRecord(QuantitySearchParamTableValuedParameterColumns);
                r.SetInt16(0, resourceTypePk);
                r.SetInt16(1, _searchParamUrlToId[(e.searchParameter.Url, e.componentIndex)]);
                if (e.compositeCorrelationId != null)
                {
                    r.SetByte(2, e.compositeCorrelationId.Value);
                }

                if (!string.IsNullOrWhiteSpace(value.System))
                {
                    r.SetString(3, value.System);
                }

                if (!string.IsNullOrWhiteSpace(value.Code))
                {
                    r.SetString(4, value.Code);
                }

                r.SetDecimal(5, value.Quantity);
                return r;
            }).ToArray();

            SqlParameter param = command.Parameters.AddWithValue("@tvpQuantitySearchParam", records.Length == 0 ? null : records);
            param.SqlDbType = SqlDbType.Structured;
            param.TypeName = "dbo.QuantitySearchParamTableType";
        }

        private void AddNumberSearchParams(ILookup<Type, (SearchParameter searchParameter, byte? componentIndex, byte? compositeCorrelationId, ISearchValue value)> lookupByType, short resourceTypePk, SqlCommand command)
        {
            var entries = lookupByType[typeof(NumberSearchValue)].ToList();

            SqlDataRecord[] records = entries.Select(e =>
            {
                var value = (NumberSearchValue)e.value;
                var r = new SqlDataRecord(NumberSearchParamTableValuedParameterColumns);
                r.SetInt16(0, resourceTypePk);
                r.SetInt16(1, _searchParamUrlToId[(e.searchParameter.Url, e.componentIndex)]);
                if (e.compositeCorrelationId != null)
                {
                    r.SetByte(2, e.compositeCorrelationId.Value);
                }

                r.SetDecimal(3, value.Number);
                return r;
            }).ToArray();

            SqlParameter param = command.Parameters.AddWithValue("@tvpNumberSearchParam", records.Length == 0 ? null : records);
            param.SqlDbType = SqlDbType.Structured;
            param.TypeName = "dbo.NumberSearchParamTableType";
        }

        private void AddUriSearchParams(ILookup<Type, (SearchParameter searchParameter, byte? componentIndex, byte? compositeCorrelationId, ISearchValue value)> lookupByType, short resourceTypePk, SqlCommand command)
        {
            var entries = lookupByType[typeof(UriSearchValue)].ToList();

            SqlDataRecord[] records = entries.Select(e =>
            {
                var value = (UriSearchValue)e.value;
                var r = new SqlDataRecord(UriSearchParamTableValuedParameterColumns);
                r.SetInt16(0, resourceTypePk);
                r.SetInt16(1, _searchParamUrlToId[(e.searchParameter.Url, e.componentIndex)]);
                if (e.compositeCorrelationId != null)
                {
                    r.SetByte(2, e.compositeCorrelationId.Value);
                }

                r.SetString(3, value.Uri);
                return r;
            }).ToArray();

            SqlParameter param = command.Parameters.AddWithValue("@tvpUriSearchParam", records.Length == 0 ? null : records);
            param.SqlDbType = SqlDbType.Structured;
            param.TypeName = "dbo.UriSearchParamTableType";
        }

        public void Build(ListedCapabilityStatement statement)
        {
            EnsureArg.IsNotNull(statement, nameof(statement));

            foreach (var resource in ModelInfo.SupportedResources)
            {
                var resourceType = (ResourceType)Enum.Parse(typeof(ResourceType), resource);
                statement.BuildRestResourceComponent(resourceType, builder =>
                {
                    builder.Versioning.Add(CapabilityStatement.ResourceVersionPolicy.NoVersion);
                    builder.Versioning.Add(CapabilityStatement.ResourceVersionPolicy.Versioned);
                    builder.Versioning.Add(CapabilityStatement.ResourceVersionPolicy.VersionedUpdate);
                    builder.ReadHistory = true;
                    builder.UpdateCreate = true;
                });
            }
        }

        public async Task<SearchResult> Search(SearchOptions searchOptions, CancellationToken cancellationToken)
        {
            using (var connection = new SqlConnection(_configuration.ConnectionString))
            {
                await connection.OpenAsync(cancellationToken);

                using (SqlCommand command = connection.CreateCommand())
                {
                    int pageSize = 10;

#pragma warning disable CA1806 // Do not ignore method results
                    int.TryParse(searchOptions.ContinuationToken, out int pageNum);
#pragma warning restore CA1806 // Do not ignore method results

                    command.CommandText = SqlQueryBuilder.BuildQuery(_resourceTypeToId, _searchParamUrlToId, searchOptions, command.Parameters, pageSize, pageNum);

                    using (SqlDataReader sqlDataReader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                    {
                        var results = new List<ResourceWrapper>();

                        while (sqlDataReader.Read())
                        {
                            string resourceTypeName = _resourceTypeIdToTypeName[sqlDataReader.GetInt16(0)];
                            string id = sqlDataReader.GetString(1);
                            int version = sqlDataReader.GetInt32(2);
                            DateTime lastModified = sqlDataReader.GetDateTime(3);
                            string rawResource;

                            using (var dataStream = sqlDataReader.GetStream(4))
                            using (var ms = new RecyclableMemoryStream(_memoryStreamManager))
                            using (var gzipStream = new GZipStream(dataStream, CompressionMode.Decompress, true))
                            {
                                await gzipStream.CopyToAsync(ms, cancellationToken);
                                rawResource = Encoding.UTF8.GetString(ms.GetBuffer().AsSpan().Slice(0, (int)ms.Length));
                            }

                            results.Add(new ResourceWrapper(
                                id,
                                version.ToString(CultureInfo.InvariantCulture),
                                resourceTypeName,
                                new RawResource(rawResource, ResourceFormat.Json),
                                new ResourceRequest("https://exmaple", HttpMethod.Get),
                                new DateTimeOffset(lastModified, TimeSpan.Zero),
                                false,
                                ImmutableArray<KeyValuePair<string, string>>.Empty));
                        }

                        return new SearchResult(results, results.Count == pageSize ? (pageNum + 1).ToString(CultureInfo.InvariantCulture) : null);
                    }
                }
            }
        }
    }
}
