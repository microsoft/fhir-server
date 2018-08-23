// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Tests.Common
{
    public static class Definitions
    {
        private const string EmbeddedResourceSubNamespace = "DefinitionFiles";

        /// <summary>
        /// Gets back a resource from a definition file.
        /// </summary>
        /// <typeparam name="T">The resource type.</typeparam>
        /// <param name="fileName">The JSON filename, omit the extension</param>
        public static Bundle GetDefinition(string fileName)
        {
            var json = EmbeddedResourceManager.GetStringContent(EmbeddedResourceSubNamespace, fileName, "json");

            var parser = new Hl7.Fhir.Serialization.FhirJsonParser();
            return (Bundle)parser.Parse(json, typeof(Bundle));
        }
    }
}
