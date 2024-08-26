// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.ComponentModel;
using System.Threading;
using Azure.Identity;
using Azure.Storage.Blobs;
using EnsureThat;
using Microsoft.Build.Framework;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    internal class SqlSecondaryStore<T>
    {
        private static readonly object _parameterLocker = new object();
        private static string _warehouseConnectionString;
        private static bool _warehouseIsSet;
        private static string _adlsContainer;
        private static string _adlsConnectionString;
        private static string _adlsAccountName;
        private static string _adlsAccountKey;
        private static Uri _adlsAccountUri;
        private static string _adlsAccountManagedIdentityClientId;
        private static BlobContainerClient _adlsClient;
        private static bool _adlsIsSet;

        public SqlSecondaryStore(ISqlRetryService sqlRetryService, ILogger<T> logger)
        {
            EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));

            if (!_warehouseIsSet)
            {
                lock (_parameterLocker)
                {
                    if (!_warehouseIsSet)
                    {
                        _warehouseConnectionString = GetStorageParameter(sqlRetryService, logger, "MergeResources.Warehouse.ConnectionString");
                        var warehouseServer = GetStorageParameter(sqlRetryService, logger, "MergeResources.Warehouse.Server");
                        if (string.IsNullOrEmpty(_warehouseConnectionString))
                        {
                            var warehouseDatabase = GetStorageParameter(sqlRetryService, logger, "MergeResources.Warehouse.Database");
                            if (!string.IsNullOrEmpty(warehouseDatabase))
                            {
                                var warehouseAuthentication = GetStorageParameter(sqlRetryService, logger, "MergeResources.Warehouse.Authentication"); // Azure VM: Active Directory Managed Identity, local: Active Directory Interactive
                                var warehouseUser = GetStorageParameter(sqlRetryService, logger, "MergeResources.Warehouse.User"); // ClientID for UAMI
                                _warehouseConnectionString = $"server={warehouseServer};database={warehouseDatabase};authentication={warehouseAuthentication};{(string.IsNullOrEmpty(warehouseUser) ? string.Empty : $"user={warehouseUser};")}Max Pool Size=300;";
                            }
                        }

                        _warehouseIsSet = true;
                    }
                }
            }

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
                        _adlsContainer = $"fhir-adls-{db.Replace("_", "-", StringComparison.InvariantCultureIgnoreCase).ToLowerInvariant()}";

                        var uriStr = GetStorageParameter(sqlRetryService, logger, "MergeResources.AdlsAccountUri");
                        if (uriStr != null)
                        {
                            _adlsAccountUri = new Uri(uriStr);
                            _adlsAccountManagedIdentityClientId = GetStorageParameter(sqlRetryService, logger, "MergeResources.AdlsAccountManagedIdentityClientId");
                        }

                        if (_adlsConnectionString != null || _adlsAccountUri != null)
                        {
                            _adlsClient = GetAdlsContainer();
                        }

                        _adlsIsSet = true;
                    }
                }
            }
        }

        public static BlobContainerClient AdlsClient => _adlsIsSet ? _adlsClient : throw new ArgumentOutOfRangeException();

        public static string AdlsContainer => _adlsIsSet ? _adlsContainer : throw new ArgumentOutOfRangeException();

        public static string AdlsAccountName => _adlsIsSet ? _adlsAccountName : throw new ArgumentOutOfRangeException();

        public static string AdlsAccountKey => _adlsIsSet ? _adlsAccountKey : throw new ArgumentOutOfRangeException();

        public static string WarehouseConnectionString => _warehouseIsSet ? _warehouseConnectionString : throw new ArgumentOutOfRangeException();

        private static string GetStorageParameter(ISqlRetryService sqlRetryService, ILogger<T> logger, string parameterId)
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
                catch (Exception)
                {
                    return null;
                }
            }
        }

        private static BlobContainerClient GetAdlsContainer() // creates if does not exist
        {
            var blobContainerClient = _adlsAccountUri != null && _adlsAccountManagedIdentityClientId != null
                                    ? new BlobContainerClient(new Uri(_adlsAccountUri, _adlsContainer), new ManagedIdentityCredential(_adlsAccountManagedIdentityClientId))
                                    : _adlsAccountUri != null
                                        ? new BlobContainerClient(new Uri(_adlsAccountUri, _adlsContainer), new InteractiveBrowserCredential())
                                        : new BlobServiceClient(_adlsConnectionString).GetBlobContainerClient(_adlsContainer);

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
