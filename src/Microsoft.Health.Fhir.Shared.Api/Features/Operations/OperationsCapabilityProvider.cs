// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.Registration;

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
        private readonly CoreFeatureConfiguration _coreFeatureConfiguration;
        private readonly ImplementationGuidesConfiguration _implementationGuidesConfiguration;
        private readonly IUrlResolver _urlResolver;
        private readonly IFhirRuntimeConfiguration _fhirRuntimeConfiguration;

        public OperationsCapabilityProvider(
            IOptions<OperationsConfiguration> operationConfiguration,
            IOptions<FeatureConfiguration> featureConfiguration,
            IOptions<CoreFeatureConfiguration> coreFeatureConfiguration,
            IOptions<ImplementationGuidesConfiguration> implementationGuidesConfiguration,
            IUrlResolver urlResolver,
            IFhirRuntimeConfiguration fhirRuntimeConfiguration)
        {
            EnsureArg.IsNotNull(operationConfiguration?.Value, nameof(operationConfiguration));
            EnsureArg.IsNotNull(featureConfiguration?.Value, nameof(featureConfiguration));
            EnsureArg.IsNotNull(coreFeatureConfiguration?.Value, nameof(coreFeatureConfiguration));
            EnsureArg.IsNotNull(implementationGuidesConfiguration?.Value, nameof(implementationGuidesConfiguration));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(fhirRuntimeConfiguration, nameof(fhirRuntimeConfiguration));

            _operationConfiguration = operationConfiguration.Value;
            _featureConfiguration = featureConfiguration.Value;
            _coreFeatureConfiguration = coreFeatureConfiguration.Value;
            _implementationGuidesConfiguration = implementationGuidesConfiguration.Value;
            _urlResolver = urlResolver;
            _fhirRuntimeConfiguration = fhirRuntimeConfiguration;
        }

        public Task BuildAsync(ICapabilityStatementBuilder builder, CancellationToken cancellationToken)
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

            if (_operationConfiguration.BulkDelete.Enabled)
            {
                builder.Apply(AddBulkDeleteDetails);
            }

            if (_operationConfiguration.BulkUpdate.Enabled)
            {
                builder.Apply(AddBulkUpdateDetails);
            }

            if (_coreFeatureConfiguration.SupportsSelectableSearchParameters)
            {
                builder.Apply(AddSelectableSearchParameterDetails);
            }

            if (_coreFeatureConfiguration.SupportsIncludes && (_fhirRuntimeConfiguration.DataStore?.Equals(KnownDataStores.SqlServer, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                builder.Apply(AddIncludesDetails);
            }

            if (_implementationGuidesConfiguration.USCore?.EnableDocRef ?? false)
            {
                builder.Apply(AddDocRefDetails);
            }

            if (_operationConfiguration.Terminology?.EnableExpand ?? false)
            {
                builder.Apply(AddExpandDetails);
            }

            return Task.CompletedTask;
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

        public void AddBulkDeleteDetails(ListedCapabilityStatement capabilityStatement)
        {
            GetAndAddOperationDefinitionUriToCapabilityStatement(capabilityStatement, OperationsConstants.BulkDelete);
        }

        public void AddBulkUpdateDetails(ListedCapabilityStatement capabilityStatement)
        {
            GetAndAddOperationDefinitionUriToCapabilityStatement(capabilityStatement, OperationsConstants.BulkUpdate);
        }

        public void AddSelectableSearchParameterDetails(ListedCapabilityStatement capabilityStatement)
        {
            GetAndAddOperationDefinitionUriToCapabilityStatement(capabilityStatement, OperationsConstants.SearchParameterStatus);
        }

        public void AddIncludesDetails(ListedCapabilityStatement capabilityStatement)
        {
            GetAndAddOperationDefinitionUriToCapabilityStatement(capabilityStatement, OperationsConstants.Includes);
        }

        public void AddDocRefDetails(ListedCapabilityStatement capabilityStatement)
        {
            GetAndAddOperationDefinitionUriToCapabilityStatement(capabilityStatement, OperationsConstants.DocRef);
        }

        public void AddExpandDetails(ListedCapabilityStatement capabilityStatement)
        {
            GetAndAddOperationDefinitionUriToCapabilityStatement(capabilityStatement, OperationsConstants.ValueSetExpand);
        }
    }
}
