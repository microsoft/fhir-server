// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures
{
    internal abstract class StoredProcedureBase : IStoredProcedure
    {
        private readonly Lazy<string> _body;
        private readonly Lazy<string> _versionedName;

        protected StoredProcedureBase()
        {
            _body = new Lazy<string>(GetBody);
            _versionedName = new Lazy<string>(() => $"{Name}_{Hash(_body.Value)}");
        }

        protected virtual string Name => CamelCase(GetType().Name);

        public string FullName => _versionedName.Value;

        public StoredProcedure AsStoredProcedure()
        {
            return new StoredProcedure
            {
                Id = FullName,
                Body = _body.Value,
            };
        }

        protected async Task<StoredProcedureResponse<T>> ExecuteStoredProc<T>(IDocumentClient client, Uri collection, string partitionId, params object[] parameters)
        {
            EnsureArg.IsNotNull(client, nameof(client));
            EnsureArg.IsNotNull(collection, nameof(collection));
            EnsureArg.IsNotNull(partitionId, nameof(partitionId));

            var partitionKey = new RequestOptions
            {
                PartitionKey = new PartitionKey(partitionId),
            };

            var results = await client.ExecuteStoredProcedureAsync<T>(
                GetUri(collection),
                partitionKey,
                parameters);

            return results;
        }

        public Uri GetUri(Uri collection)
        {
            return new Uri($"{collection}/sprocs/{Uri.EscapeUriString(FullName)}", UriKind.Relative);
        }

        private string GetBody()
        {
            // Assumed convention is the stored proc is in the same directory as the cs file
            var resourceName = $"{GetType().Namespace}.{Name}.js";

            using (Stream resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            using (var reader = new StreamReader(resourceStream))
            {
                return reader.ReadToEnd();
            }
        }

        private static string Hash(string data)
        {
            EnsureArg.IsNotEmpty(data, nameof(data));

            using (var sha256 = new SHA256Managed())
            {
                var hashed = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
                return BitConverter.ToString(hashed).Replace("-", string.Empty, StringComparison.Ordinal);
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
