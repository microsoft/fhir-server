// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CsvHelper;

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public class ModelProvider
    {
        public Dictionary<string, short> ResourceTypeMapping { get; private set; }

        public Dictionary<Uri, short> SearchParamTypeMapping { get; private set; }

        public static ModelProvider CreateModelProvider()
        {
            using var typeReader = new StreamReader("ResourceTypes.csv");
            using var typeCsv = new CsvReader(typeReader, CultureInfo.InvariantCulture);
            Dictionary<string, short> resourceTypeMapping = typeCsv.GetRecords<ResourceType>().ToDictionary(t => t.Name, t => t.ResourceTypeId);

            using var searchParamReader = new StreamReader("ParamMappings.csv");
            using var searchParamCsv = new CsvReader(searchParamReader, CultureInfo.InvariantCulture);
            Dictionary<Uri, short> searchParamTypeMapping = searchParamCsv.GetRecords<SearchParamMetadata>().ToDictionary(t => new Uri(t.Uri), t => t.SearchParamId);

            return new ModelProvider()
            {
                ResourceTypeMapping = resourceTypeMapping,
                SearchParamTypeMapping = searchParamTypeMapping,
            };
        }
    }
}
