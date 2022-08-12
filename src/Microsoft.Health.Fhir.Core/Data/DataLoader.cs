// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.Reflection;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Data
{
    public static class DataLoader
    {
        private static readonly string _thisNamespace = typeof(DataLoader).Namespace;
        private static readonly Assembly _thisAssembly = typeof(DataLoader).Assembly;

        public static Stream OpenVersionedFileStream(this IModelInfoProvider modelInfoProvider, string filename, string @namespace = null, Assembly assembly = null)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            string manifestName = $"{@namespace ?? _thisNamespace}.{modelInfoProvider.Version}.{filename}";
            return (assembly ?? _thisAssembly).GetManifestResourceStream(manifestName);
        }

        public static Stream OpenOperationDefinitionFileStream(string filename, string @namespace = null, Assembly assembly = null)
        {
            string manifestName = $"{@namespace ?? _thisNamespace}.OperationDefinition.{filename}";
            return (assembly ?? _thisAssembly).GetManifestResourceStream(manifestName);
        }
    }
}
