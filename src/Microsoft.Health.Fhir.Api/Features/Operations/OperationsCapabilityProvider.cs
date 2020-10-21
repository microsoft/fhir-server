// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Routing;

namespace Microsoft.Health.Fhir.Api.Features.Operations
{
    /// <summary>
    /// Class that handles adding details of the supported operations
    /// to the capability statement of the fhir-server.
    /// </summary>
    public class OperationsCapabilityProvider : IProvideCapability
    {
        private readonly OperationsConfiguration _operationConfiguration;
        private readonly FeatureConfiguration _featureConfiguration;
        private readonly IUrlResolver _urlResolver;

        public OperationsCapabilityProvider(
            IOptions<OperationsConfiguration> operationConfiguration,
            IOptions<FeatureConfiguration> featureConfiguration,
            IUrlResolver urlResolver)
        {
            EnsureArg.IsNotNull(operationConfiguration?.Value, nameof(operationConfiguration));
            EnsureArg.IsNotNull(featureConfiguration?.Value, nameof(featureConfiguration));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));

            _operationConfiguration = operationConfiguration.Value;
            _featureConfiguration = featureConfiguration.Value;
            _urlResolver = urlResolver;
        }

        public void Build(ICapabilityStatementBuilder builder)
        {
            if (_operationConfiguration.Export.Enabled)
            {
                builder.Update(AddExportDetails);
            }

            if (_operationConfiguration.Reindex.Enabled)
            {
                builder.Update(AddReindexDetails);
            }

            if (_featureConfiguration.SupportsAnonymizedExport)
            {
                builder.Update(AddAnonymizedExportDetails);
            }
        }

        public void AddExportDetails(ListedCapabilityStatement capabilityStatement)
        {
            GetAndAddOperationDefinitionUriToCapabilityStatement(capabilityStatement, OperationsConstants.Export);
            GetAndAddOperationDefinitionUriToCapabilityStatement(capabilityStatement, OperationsConstants.PatientExport);
            GetAndAddOperationDefinitionUriToCapabilityStatement(capabilityStatement, OperationsConstants.GroupExport);
        }

        public void AddAnonymizedExportDetails(ListedCapabilityStatement capabilityStatement)
        {
            GetAndAddOperationDefinitionUriToCapabilityStatement(capabilityStatement, OperationsConstants.AnonymizedExport);
        }

        public void AddReindexDetails(ListedCapabilityStatement capabilityStatement)
        {
            GetAndAddOperationDefinitionUriToCapabilityStatement(capabilityStatement, OperationsConstants.Reindex);
            GetAndAddOperationDefinitionUriToCapabilityStatement(capabilityStatement, OperationsConstants.ResourceReindex);
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
    }
}
