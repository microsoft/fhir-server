// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Rest;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Export;
using StringExtensions = Microsoft.Health.Core.Extensions.StringExtensions;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    /// <summary>
    /// MediatR request handler. Called when the ExportController creates an export job.
    /// </summary>
    public class CreateExportRequestHandler : IRequestHandler<CreateExportRequest, CreateExportResponse>
    {
        private readonly IClaimsExtractor _claimsExtractor;
        private readonly IFhirOperationDataStore _fhirOperationDataStore;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly ExportJobConfiguration _exportJobConfiguration;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly ISearchOptionsFactory _searchOptionsFactory;
        private readonly ILogger<CreateExportRequestHandler> _logger;
        private readonly bool _includeValidateTypeFiltersValidationDetails;

        public CreateExportRequestHandler(
            IClaimsExtractor claimsExtractor,
            IFhirOperationDataStore fhirOperationDataStore,
            IAuthorizationService<DataActions> authorizationService,
            IOptions<ExportJobConfiguration> exportJobConfiguration,
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            ISearchOptionsFactory searchOptionsFactory,
            ILogger<CreateExportRequestHandler> logger,
            bool includeValidateTypeFiltersValidationDetails = false)
        {
            EnsureArg.IsNotNull(claimsExtractor, nameof(claimsExtractor));
            EnsureArg.IsNotNull(fhirOperationDataStore, nameof(fhirOperationDataStore));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(exportJobConfiguration));
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(searchOptionsFactory, nameof(searchOptionsFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _claimsExtractor = claimsExtractor;
            _fhirOperationDataStore = fhirOperationDataStore;
            _authorizationService = authorizationService;
            _exportJobConfiguration = exportJobConfiguration.Value;
            _contextAccessor = fhirRequestContextAccessor;
            _searchOptionsFactory = searchOptionsFactory;
            _logger = logger;
            _includeValidateTypeFiltersValidationDetails = includeValidateTypeFiltersValidationDetails;
        }

        public async Task<CreateExportResponse> Handle(CreateExportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Export, cancellationToken) != DataActions.Export)
            {
                throw new UnauthorizedFhirActionException();
            }

            var requestorClaims = _claimsExtractor.Extract()?.OrderBy(claim => claim.Key, StringComparer.Ordinal).ToList();

            string storageAccountConnectionHash = string.IsNullOrEmpty(_exportJobConfiguration.StorageAccountConnection) ?
                string.Empty :
                StringExtensions.ComputeHash(_exportJobConfiguration.StorageAccountConnection);

            var filters = ParseFilter(request.Filters);
            ValidateTypeFilters(filters);

            ExportJobFormatConfiguration formatConfiguration = ParseFormat(request.FormatName, request.ContainerName != null);

            uint maxCount = request.MaxCount > 0 ? request.MaxCount : _exportJobConfiguration.MaximumNumberOfResourcesPerQuery;

            var jobRecord = new ExportJobRecord(
                requestUri: request.RequestUri,
                exportType: request.RequestType,
                exportFormat: formatConfiguration.Format,
                resourceType: request.ResourceType,
                filters: filters,
                hash: "N/A",
                rollingFileSizeInMB: _exportJobConfiguration.RollingFileSizeInMB,
                requestorClaims: requestorClaims,
                since: request.Since,
                till: request.Till,
                groupId: request.GroupId,
                storageAccountConnectionHash: storageAccountConnectionHash,
                storageAccountUri: _exportJobConfiguration.StorageAccountUri,
                anonymizationConfigurationCollectionReference: request.AnonymizationConfigurationCollectionReference,
                anonymizationConfigurationLocation: request.AnonymizationConfigurationLocation,
                anonymizationConfigurationFileETag: request.AnonymizationConfigurationFileETag,
                maximumNumberOfResourcesPerQuery: maxCount > 0 ? maxCount : _exportJobConfiguration.MaximumNumberOfResourcesPerQuery,
                numberOfPagesPerCommit: _exportJobConfiguration.NumberOfPagesPerCommit,
                storageAccountContainerName: request.ContainerName,
                isParallel: request.IsParallel,
                includeHistory: request.IncludeHistory,
                includeDeleted: request.IncludeDeleted,
                smartRequest: _contextAccessor?.RequestContext?.AccessControlContext?.ApplyFineGrainedAccessControl == true);

            var outcome = await _fhirOperationDataStore.CreateExportJobAsync(jobRecord, cancellationToken);

            return new CreateExportResponse(outcome.JobRecord.Id);
        }

        /// <summary>
        /// Parses the _typeFilter parameter from a string into a list of <see cref="ExportJobFilter"/> objects.
        /// </summary>
        /// <param name="filterString">The _typeFilter parameter input.</param>
        /// <returns>A list of <see cref="ExportJobFilter"/></returns>
        private static List<ExportJobFilter> ParseFilter(string filterString)
        {
            var filters = new List<ExportJobFilter>();

            if (!string.IsNullOrWhiteSpace(filterString))
            {
                var filterArray = filterString.Split(",");
                foreach (string filter in filterArray)
                {
                    var parameterIndex = filter.IndexOf('?', StringComparison.Ordinal);

                    if (parameterIndex < 0 || parameterIndex == filter.Length - 1)
                    {
                        throw new BadRequestException(string.Format(Core.Resources.TypeFilterUnparseable, filter));
                    }

                    var filterType = filter.Substring(0, parameterIndex);

                    var filterParameters = filter.Substring(parameterIndex + 1).Split("&");
                    var parameterTupleList = new List<Tuple<string, string>>();

                    foreach (string parameter in filterParameters)
                    {
                        var keyValue = parameter.Split("=");

                        if (keyValue.Length != 2)
                        {
                            throw new BadRequestException(string.Format(Core.Resources.TypeFilterUnparseable, filter));
                        }

                        parameterTupleList.Add(new Tuple<string, string>(keyValue[0], keyValue[1]));
                    }

                    filters.Add(new ExportJobFilter(filterType, parameterTupleList));
                }
            }

            return filters;
        }

        private ExportJobFormatConfiguration ParseFormat(string formatName, bool useContainer)
        {
            ExportJobFormatConfiguration formatConfiguration = null;

            if (formatName != null)
            {
                formatConfiguration = _exportJobConfiguration.Formats?.FirstOrDefault(
                (ExportJobFormatConfiguration formatConfig) => formatConfig.Name.Equals(formatName, StringComparison.OrdinalIgnoreCase));

                if (formatConfiguration == null)
                {
                    throw new BadRequestException(string.Format(Core.Resources.ExportFormatNotFound, formatName));
                }
            }

            formatConfiguration ??= _exportJobConfiguration.Formats?.FirstOrDefault(
                (ExportJobFormatConfiguration formatConfig) => formatConfig.Default);

            formatConfiguration ??= new ExportJobFormatConfiguration()
            {
                Format = useContainer ?
                    $"{ExportFormatTags.Timestamp}-{ExportFormatTags.Id}/{ExportFormatTags.ResourceName}" :
                    $"{ExportFormatTags.ResourceName}",
            };

            return formatConfiguration;
        }

        private void ValidateTypeFilters(List<ExportJobFilter> filters)
        {
            if (_contextAccessor.GetHandlingHeader() == SearchParameterHandling.Lenient)
            {
                _logger.LogInformation("Validation skipped due to opting for Lenient error handling.");
                return;
            }

            if (filters == null || filters.Count == 0)
            {
                _logger.LogInformation("No type filters to validate.");
                return;
            }

            var errors = new List<string[]>();
            foreach (var filter in filters)
            {
                if (filter.Parameters == null || filter.Parameters.Count == 0)
                {
                    continue;
                }

                var searchOptions = _searchOptionsFactory.Create(
                    filter.ResourceType,
                    new ReadOnlyCollection<Tuple<string, string>>(filter.Parameters));
                foreach (var parameter in searchOptions.UnsupportedSearchParams)
                {
                    errors.Add(new string[]
                        {
                            filter.ResourceType,
                            parameter.Item1,
                        });
                }
            }

            if (errors.Count > 0)
            {
                var errorMessage = new StringBuilder($"{errors.Count} invalid search parameter(s) found:{Environment.NewLine}");
                errors.ForEach(e => errorMessage.AppendLine(
                    string.Format(CultureInfo.InvariantCulture, "[type: {0}, parameter: {1}]", e[0], e[1])));

                var message = errorMessage.ToString();
                _logger.LogError(message);

                var ex = new BadRequestException(message);
                if (_includeValidateTypeFiltersValidationDetails)
                {
                    // Note: Test purpose only
                    ex.Data.Add(nameof(ValidateTypeFilters), errors);
                }

                throw ex;
            }
        }
    }
}
