// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using Azure.Identity;
using Azure.Storage.Blobs;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    internal class SqlAdlsClient
    {
        private static readonly object _parameterLocker = new object();
        private static string _adlsContainerName;
        private static string _adlsConnectionString;
        private static string _adlsAccountName;
        private static string _adlsAccountKey;
        private static Uri _adlsAccountUri;
        private static string _adlsAccountManagedIdentityClientId;
        private static BlobContainerClient _adlsContainer;
        private static bool _adlsIsSet;

        public SqlAdlsClient(ISqlRetryService sqlRetryService, ILogger logger)
        {
            EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));

            if (!_adlsIsSet)
            {
                lock (_parameterLocker)
                {
                    if (!_adlsIsSet)
                    {
                        _adlsAccountName = GetStorageParameter(sqlRetryService, logger, "MergeResources.AdlsAccountName");
                        _adlsConnectionString = GetStorageParameter(sqlRetryService, logger, "MergeResources.AdlsConnectionString");
                        if (_adlsAccountName != null)
                        {
                            _adlsAccountKey = GetStorageParameter(sqlRetryService, logger, "MergeResources.AdlsAccountKey");
                            _adlsConnectionString = $"DefaultEndpointsProtocol=https;AccountName={_adlsAccountName};AccountKey={_adlsAccountKey};EndpointSuffix=core.windows.net";
                        }

                        var db = sqlRetryService.Database.Length < 50 ? sqlRetryService.Database : sqlRetryService.Database.Substring(0, 50);
                        _adlsContainerName = $"fhir-adls-{db.Replace("_", "-", StringComparison.InvariantCultureIgnoreCase).ToLowerInvariant()}";

                        var uriStr = GetStorageParameter(sqlRetryService, logger, "MergeResources.AdlsAccountUri");
                        if (uriStr != null)
                        {
                            _adlsAccountUri = new Uri(uriStr);
                            _adlsAccountManagedIdentityClientId = GetStorageParameter(sqlRetryService, logger, "MergeResources.AdlsAccountManagedIdentityClientId");
                        }

                        if (_adlsConnectionString != null || _adlsAccountUri != null)
                        {
                            _adlsContainer = GetContainer();
                        }

                        logger.LogInformation($"Storage container is set to {(_adlsContainer == null ? "NULL" : _adlsContainerName)}");
                        sqlRetryService.TryLogEvent("SqlAdlsClient", "Warn", $"Storage container is set to {(_adlsContainer == null ? "NULL" : _adlsContainerName)}", null, CancellationToken.None).Wait();

                        _adlsIsSet = true;
                    }
                }
            }
        }

        public static BlobContainerClient Container => _adlsIsSet ? _adlsContainer : throw new ArgumentOutOfRangeException();

        public static string AdlsContainerName => _adlsIsSet ? _adlsContainerName : throw new ArgumentOutOfRangeException();

        public static string AdlsAccountName => _adlsIsSet ? _adlsAccountName : throw new ArgumentOutOfRangeException();

        public static string AdlsAccountKey => _adlsIsSet ? _adlsAccountKey : throw new ArgumentOutOfRangeException();

        private static string GetStorageParameter(ISqlRetryService sqlRetryService, ILogger logger, string parameterId)
        {
            lock (_parameterLocker)
            {
                try
                {
                    using var cmd = new SqlCommand();
                    cmd.CommandText = "IF object_id('dbo.Parameters') IS NOT NULL SELECT Char FROM dbo.Parameters WHERE Id = @Id";
                    cmd.Parameters.AddWithValue("@Id", parameterId);
                    var value = cmd.ExecuteScalarAsync(sqlRetryService, logger, CancellationToken.None).Result;
                    return value == null ? null : (string)value;
                }
                catch (Exception e)
                {
                    logger.LogError(e, $"Failed to get storage parameter from Parameters.{parameterId}");
                    throw;
                }
            }
        }

        private static BlobContainerClient GetContainer() // creates if does not exist
        {
            var blobContainerClient = _adlsAccountUri != null && _adlsAccountManagedIdentityClientId != null
                                    ? new BlobContainerClient(new Uri(_adlsAccountUri, _adlsContainerName), new ManagedIdentityCredential(_adlsAccountManagedIdentityClientId))
                                    : _adlsAccountUri != null
                                        ? new BlobContainerClient(new Uri(_adlsAccountUri, _adlsContainerName), new InteractiveBrowserCredential())
                                        : new BlobServiceClient(_adlsConnectionString).GetBlobContainerClient(_adlsContainerName);

            if (!blobContainerClient.Exists())
            {
                lock (_parameterLocker)
                {
                    blobContainerClient.CreateIfNotExists();
                }
            }

            return blobContainerClient;
        }
    }
}
