// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Internal.Fhir.RegisterAndMonitorImport
{
    internal sealed class RegisterAndMonitorConfiguration
    {
        public const string SectionName = "RegisterAndMonitor";

        /// <summary>
        /// If you just want to monitor an import then provide a url for this key/value
        /// Note that if this is provided then it only performs this operation and will not do any import.
        /// The endpoint would be something similar to this https://{your fhir endpoint}/_operations/import/{import id}
        /// </summary>
        public string MonitorImportStatusEndpoint { get; set; } = string.Empty;

        /// <summary>
        /// the time delay between calls to get the status of the import
        /// generally smaller ndjson files can be used with smaller timespans
        /// and larger ndjson files should have larger timespans
        /// </summary>
        public TimeSpan ImportStatusDelay { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Azure blob storage connection string
        /// </summary>
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// Azure blob storage container name - used with the ConnectionString
        /// </summary>
        public string ContainerName { get; set; } = string.Empty;

        /// <summary>
        /// the type of file it'll search for in the container
        /// do not add a file extension - It's assumed and added to the ResourceType automatically
        /// </summary>
        public string ResourceType { get; set; } = string.Empty;

        /// <summary>
        /// a simple flag to indicate that when true we're going to use the token for/// our http POST/GET
        /// this is useful when you have a Paas deployment if you set to false then you could use it against an oss fhir service
        /// and Token properties do not need to be set.
        /// </summary>
        public bool UseBearerToken { get; set; }

        /// <summary>
        /// the number of blobs we want to import at a time
        /// </summary>
        public int NumberOfBlobsForImport { get; set; } = 1;

        /// <summary>
        /// the url of your FHIR endpoint - also used as the value for the key "resource" for getting a token
        /// </summary>
        public string FhirEndpoint { get; set; }

        /// <summary>
        /// Used for the value of the key "grant_type" for getting a token
        /// </summary>
        public string TokenGrantType { get; set; } = "Client_Credentials";

        /// <summary>
        /// Used for the value of the key "client_id" for getting a token
        /// </summary>
        public string TokenClientId { get; set; }

        /// <summary>
        /// Used for the value of the key "client_secret" for getting a token
        /// </summary>
        public string TokenClientSecret { get; set; }

        public bool IsMonitorImportStatusEndpoint => !string.IsNullOrWhiteSpace(MonitorImportStatusEndpoint);
    }
}
