// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

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
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;

        public OperationsCapabilityProvider(
            IOptions<OperationsConfiguration> operationConfiguration,
            IOptions<FeatureConfiguration> featureConfiguration,
            IUrlResolver urlResolver,
            Func<IScoped<ISearchService>> searchServiceFactory)
        {
            EnsureArg.IsNotNull(operationConfiguration?.Value, nameof(operationConfiguration));
            EnsureArg.IsNotNull(featureConfiguration?.Value, nameof(featureConfiguration));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));

            _operationConfiguration = operationConfiguration.Value;
            _featureConfiguration = featureConfiguration.Value;
            _urlResolver = urlResolver;
            _searchServiceFactory = searchServiceFactory;
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
            builder.Apply(AddPatientEverythingDetails);
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

        public void AddPatientEverythingDetails(ListedCapabilityStatement capabilityStatement)
        {
            using IScoped<ISearchService> search = _searchServiceFactory();

            // Will remove this when enabled in SQL Server
            if (string.Equals(search.Value.GetType().Name, "SqlServerSearchService", StringComparison.Ordinal))
            {
                return;
            }

            if (ModelInfoProvider.Version.Equals(FhirSpecification.Stu3))
            {
                capabilityStatement.Rest.Server().Operation.Add(new OperationComponent
                {
                    Name = OperationTypes.PatientEverything,
                    Definition = new ReferenceComponent
                    {
                        Reference = OperationTypes.PatientEverythingUri,
                    },
                });
            }
            else
            {
                capabilityStatement.Rest.Server().Operation.Add(new OperationComponent
                {
                    Name = OperationTypes.PatientEverything,
                    Definition = OperationTypes.PatientEverythingUri,
                });
            }
        }
    }
}
