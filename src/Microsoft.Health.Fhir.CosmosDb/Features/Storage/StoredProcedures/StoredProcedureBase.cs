// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Scripts;
using Microsoft.Health.Core.Extensions;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures
{
    public abstract class StoredProcedureBase : IStoredProcedure
    {
        private readonly Lazy<string> _body;
        private readonly Lazy<string> _versionedName;

        protected StoredProcedureBase()
        {
            _body = new Lazy<string>(GetBody);
            _versionedName = new Lazy<string>(() => $"{Name}_{_body.Value.ComputeHash()}");
        }

        protected virtual string Name => CamelCase(GetType().Name);

        public string FullName => _versionedName.Value;

        public StoredProcedureProperties ToStoredProcedureProperties()
        {
            return new StoredProcedureProperties
            {
                Id = FullName,
                Body = _body.Value,
            };
        }

        protected async Task<StoredProcedureExecuteResponse<T>> ExecuteStoredProc<T>(Scripts client, string partitionId, CancellationToken cancellationToken, params object[] parameters)
        {
            EnsureArg.IsNotNull(client, nameof(client));
            EnsureArg.IsNotNull(partitionId, nameof(partitionId));

            StoredProcedureExecuteResponse<T> results = await client.ExecuteStoredProcedureAsync<T>(
                    FullName,
                    new PartitionKey(partitionId),
                    parameters,
                    cancellationToken: cancellationToken);

            return results;
        }

        public Uri GetUri(Uri collection)
        {
            return new Uri($"{collection}/sprocs/{Uri.EscapeDataString(FullName)}", UriKind.Relative);
        }

        private string GetBody()
        {
            // Assumed convention is the stored proc is in the same directory as the cs file
            var resourceName = $"{GetType().Namespace}.{Name}.js";

            using (Stream resourceStream = GetType().Assembly.GetManifestResourceStream(resourceName))
            using (var reader = new StreamReader(resourceStream))
            {
                return reader.ReadToEnd();
            }
        }

        private static string CamelCase(string str)
        {
            EnsureArg.IsNotEmpty(str, nameof(str));
            EnsureArg.IsTrue(str.Length > 1, nameof(str));

            return string.Concat(char.ToLowerInvariant(str[0]), str.Substring(1));
        }
    }
}
