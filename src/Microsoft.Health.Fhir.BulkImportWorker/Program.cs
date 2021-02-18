// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.BulkImportWorker
{
    internal class Program
    {
        public static async Task Main(string[] args)
        {
            args.ToString();

            IModelInfoProvider provider = new VersionSpecificModelInfoProvider();
            SearchParameterDefinitionManager definitionManager = new SearchParameterDefinitionManager(provider);
            await definitionManager.StartAsync(CancellationToken.None);
            SupportedSearchParameterDefinitionManager supportedSearchParameterDefinitionManager
                = new SupportedSearchParameterDefinitionManager(definitionManager);

            // FhirNodeToSearchValueTypeConverterManager fhirNodeToSearchValueTypeConverterManager
            //    = new FhirNodeToSearchValueTypeConverterManager(await CreateFhirElementToSearchValueTypeConverterManagerAsync());

            await Task.CompletedTask;
        }
    }
}
