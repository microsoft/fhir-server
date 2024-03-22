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

namespace Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.StoredProcedures
{
    public abstract class StoredProcedureMetadataBase : IStoredProcedureMetadata
    {
        private readonly Lazy<string> _body;
        private readonly Lazy<string> _versionedName;

        protected StoredProcedureMetadataBase()
        {
            _body = new Lazy<string>(GetBody);
            _versionedName = new Lazy<string>(() => $"{Name}_{_body.Value.ComputeHash()}");
        }

        public virtual string Name => CamelCase(RemoveSuffix(GetType().Name, "Metadata"));

        public string FullName => _versionedName.Value;

        public StoredProcedureProperties ToStoredProcedureProperties()
        {
            return new StoredProcedureProperties
            {
                Id = FullName,
                Body = _body.Value,
            };
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

        private static string RemoveSuffix(string str, string suffix)
        {
            if (str.EndsWith(suffix, StringComparison.Ordinal))
            {
                return str.Substring(0, str.Length - suffix.Length);
            }

            return str;
        }
    }
}
