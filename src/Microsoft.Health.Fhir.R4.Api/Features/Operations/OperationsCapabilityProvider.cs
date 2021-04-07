// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.Operations
{
    /// <summary>
    /// Class that handles FHIR R4 specific behavior for adding export operation
    /// details to the capability statement.
    /// </summary>
    public partial class OperationsCapabilityProvider
    {
        private void AddExportDetailsHelper(ICapabilityStatementBuilder builder)
        {
            AddResourceSpecificExportDetails(builder, OperationsConstants.PatientExport, KnownResourceTypes.Patient);
            AddResourceSpecificExportDetails(builder, OperationsConstants.GroupExport, KnownResourceTypes.Group);
            builder.Apply(AddExportDetails);
        }

        public void AddExportDetails(ListedCapabilityStatement capabilityStatement)
        {
            GetAndAddOperationDefinitionUriToCapabilityStatement(capabilityStatement, OperationsConstants.Export);
        }

        private void GetAndAddOperationDefinitionUriToCapabilityStatement(ListedCapabilityStatement capabilityStatement, string operationType)
        {
            Uri operationDefinitionUri = _urlResolver.ResolveOperationDefinitionUrl(operationType);
            capabilityStatement.Rest.Server().Operation.Add(new OperationComponent()
            {
                Name = operationType,
                Definition = operationDefinitionUri.ToString(),
            });
        }

        private void AddResourceSpecificExportDetails(ICapabilityStatementBuilder builder, string operationType, string resourceType)
        {
            Uri operationDefinitionUri = _urlResolver.ResolveOperationDefinitionUrl(operationType);
            builder.ApplyToResource(resourceType, resourceComponent =>
            {
                resourceComponent.Operation.Add(new OperationComponent()
                {
                    Name = operationType,
                    Definition = operationDefinitionUri.ToString(),
                });
            });
        }
    }
}
