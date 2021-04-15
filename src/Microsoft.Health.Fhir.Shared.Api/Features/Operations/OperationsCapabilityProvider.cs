// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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
    /// Class that handles adding details of the supported operationsto the capability
    /// statement of the fhir-server. This class is split across the different
    /// FHIR versions since the OperationDefinition has a different format
    /// for STU3 compared to R4 and R5.
    /// </summary>
    public partial class OperationsCapabilityProvider : IProvideCapability
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
                AddExportDetailsHelper(builder);
            }

            if (_operationConfiguration.Reindex.Enabled)
            {
                builder.Apply(AddReindexDetails);
            }

            if (_featureConfiguration.SupportsAnonymizedExport)
            {
                builder.Apply(AddAnonymizedExportDetails);
            }

            builder.Apply(AddMemberMatchDetails);
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

        public void AddMemberMatchDetails(ListedCapabilityStatement capabilityStatement)
        {
            GetAndAddOperationDefinitionUriToCapabilityStatement(capabilityStatement, OperationsConstants.MemberMatch);
        }
    }
}
