// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.Reflection;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Tests.Common
{
    public static class EmbeddedResourceManager
    {
        public static string GetStringContent(string embeddedResourceSubNamespace, string fileName, string extension)
        {
            string resourceName = $"{typeof(EmbeddedResourceManager).Namespace}.{embeddedResourceSubNamespace}.{ModelInfoProvider.Version}.{fileName}.{extension}";

            var resourceInfo = Assembly.GetExecutingAssembly().GetManifestResourceInfo(resourceName);

            if (resourceInfo == null && ModelInfoProvider.Version == FhirSpecification.R4B)
            {
                // Try R4 version
                resourceName = $"{typeof(EmbeddedResourceManager).Namespace}.{embeddedResourceSubNamespace}.R4.{fileName}.{extension}";
                resourceInfo = Assembly.GetExecutingAssembly().GetManifestResourceInfo(resourceName);
            }

            if (resourceInfo == null)
            {
                // There isn't a version specific over-ride for the requested test file, attempt to find it in the Normative test files.
                resourceName = $"{typeof(EmbeddedResourceManager).Namespace}.{embeddedResourceSubNamespace}.Normative.{fileName}.{extension}";
            }

            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
