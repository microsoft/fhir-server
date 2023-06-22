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
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.Operations
{
    /// <summary>
    /// Class that handles adding details of the supported operations to the capability
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

            if (_operationConfiguration.ConvertData.Enabled)
            {
                builder.Apply(AddConvertDataDetails);
            }

            if (_featureConfiguration.SupportsAnonymizedExport)
            {
                builder.Apply(AddAnonymizedExportDetails);
            }

            builder.Apply(AddMemberMatchDetails);
            builder.Apply(AddPatientEverythingDetails);
            builder.Apply(AddSelectableSearchParameterDetails);
        }

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

        public static void AddPatientEverythingDetails(ListedCapabilityStatement capabilityStatement)
        {
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
                Definition = new ReferenceComponent
                {
                    Reference = operationDefinitionUri.ToString(),
                },
            });
        }

        public void AddAnonymizedExportDetails(ListedCapabilityStatement capabilityStatement)
        {
            GetAndAddOperationDefinitionUriToCapabilityStatement(capabilityStatement, OperationsConstants.AnonymizedExport);
        }

        public void AddConvertDataDetails(ListedCapabilityStatement capabilityStatement)
        {
            GetAndAddOperationDefinitionUriToCapabilityStatement(capabilityStatement, OperationsConstants.ConvertData);
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

        public void AddSelectableSearchParameterDetails(ListedCapabilityStatement capabilityStatement)
        {
            GetAndAddOperationDefinitionUriToCapabilityStatement(capabilityStatement, OperationsConstants.SearchParameterStatus);
        }
    }
}
