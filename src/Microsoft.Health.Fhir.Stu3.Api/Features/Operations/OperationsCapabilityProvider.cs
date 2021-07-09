// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.Operations
{
    /// <summary>
    /// Class that handles FHIR STU3 specific behavior for adding export operation
    /// details to the capability statement.
    /// </summary>
    public partial class OperationsCapabilityProvider
    {
        private void AddExportDetailsHelper(ICapabilityStatementBuilder builder)
        {
            builder.Apply(AddExportDetails);
        }

        public void AddExportDetails(ListedCapabilityStatement capabilityStatement)
        {
            GetAndAddOperationDefinitionUriToCapabilityStatement(capabilityStatement, OperationsConstants.Export);
            GetAndAddOperationDefinitionUriToCapabilityStatement(capabilityStatement, OperationsConstants.PatientExport);
            GetAndAddOperationDefinitionUriToCapabilityStatement(capabilityStatement, OperationsConstants.GroupExport);
        }

        public void AddPatientEverythingDetails(ListedCapabilityStatement capabilityStatement)
        {
            using IScoped<ISearchService> search = _searchServiceFactory();

            // Will remove this when enabled in SQL Server
            if (string.Equals(search.Value.GetType().Name, "SqlServerSearchService", StringComparison.Ordinal))
            {
                return;
            }

            capabilityStatement.Rest.Server().Operation.Add(new OperationComponent
            {
                Name = OperationsConstants.PatientEverything,
                Definition = new ReferenceComponent
                {
                    Reference = OperationsConstants.PatientEverythingUri,
                },
            });
        }

        private void GetAndAddOperationDefinitionUriToCapabilityStatement(ListedCapabilityStatement capabilityStatement, string operationType)
        {
            Uri operationDefinitionUri = _urlResolver.ResolveOperationDefinitionUrl(operationType);
            capabilityStatement.Rest.Server().Operation.Add(new OperationComponent()
            {
                Name = operationType,
                Definition = new ReferenceComponent()
                {
                    Reference = operationDefinitionUri.ToString(),
                },
            });
        }
    }
}
