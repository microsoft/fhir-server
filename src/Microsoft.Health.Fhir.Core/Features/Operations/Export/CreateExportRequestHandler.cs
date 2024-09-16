// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Utility;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;
using StringExtensions = Microsoft.Health.Core.Extensions.StringExtensions;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    /// <summary>
    /// MediatR request handler. Called when the ExportController creates an export job.
    /// </summary>
    public class CreateExportRequestHandler : IRequestHandler<CreateExportRequest, CreateExportResponse>
    {
        private static readonly HashSet<string> KnownSearchParameterModifiers = new HashSet<string>(
            Enum.GetNames(typeof(SearchModifierCode)).Select(e => ((SearchModifierCode)Enum.Parse(typeof(SearchModifierCode), e)).GetLiteral()),
            StringComparer.OrdinalIgnoreCase);

        private readonly IClaimsExtractor _claimsExtractor;
        private readonly IFhirOperationDataStore _fhirOperationDataStore;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly ExportJobConfiguration _exportJobConfiguration;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ILogger<CreateExportRequestHandler> _logger;
        private readonly bool _includeValidateTypeFiltersValidationDetails;

        public CreateExportRequestHandler(
            IClaimsExtractor claimsExtractor,
            IFhirOperationDataStore fhirOperationDataStore,
            IAuthorizationService<DataActions> authorizationService,
            IOptions<ExportJobConfiguration> exportJobConfiguration,
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            ILogger<CreateExportRequestHandler> logger,
            bool includeValidateTypeFiltersValidationDetails = false)
        {
            EnsureArg.IsNotNull(claimsExtractor, nameof(claimsExtractor));
            EnsureArg.IsNotNull(fhirOperationDataStore, nameof(fhirOperationDataStore));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(exportJobConfiguration));
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _claimsExtractor = claimsExtractor;
            _fhirOperationDataStore = fhirOperationDataStore;
            _authorizationService = authorizationService;
            _exportJobConfiguration = exportJobConfiguration.Value;
            _contextAccessor = fhirRequestContextAccessor;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
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

        private void ValidateTypeFilters(IList<ExportJobFilter> filters)
        {
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

                foreach (var parameter in filter.Parameters)
                {
                    string name = null;
                    string modifier = null;
                    if (!TrySplitParameterName(parameter.Item1, out name, out modifier)
                        || string.IsNullOrWhiteSpace(name)
                        || (!string.IsNullOrWhiteSpace(modifier) ? !KnownSearchParameterModifiers.Contains(modifier) : false))
                    {
                        errors.Add(new string[] { filter.ResourceType, parameter.Item1, "Unknown" });
                        continue;
                    }

                    // NOTE: some search parameters loaded into SearchParameterDefinitionManager's cache from the embedded resource (search-parameters.json)
                    //       have the status with "0", inconsistent with the status in the data store. Allow the "0" status as Enabled for now.
                    SearchParameterInfo searchParameter = null;
                    if (_searchParameterDefinitionManager.TryGetSearchParameter(filter.ResourceType, name, out searchParameter)
                        && (searchParameter.SearchParameterStatus == SearchParameterStatus.Enabled
                        || searchParameter.SearchParameterStatus == 0))
                    {
                        continue;
                    }

                    errors.Add(new string[]
                        {
                            filter.ResourceType,
                            parameter.Item1,
                            searchParameter?.SearchParameterStatus.ToString() ?? "Unknown",
                        });
                }
            }

            if (errors.Count > 0)
            {
                var errorMessage = new StringBuilder($"{errors.Count} invalid search parameter(s) found:{Environment.NewLine}");
                errors.ForEach(e => errorMessage.AppendLine(
                    string.Format(CultureInfo.InvariantCulture, "[type: {0}, parameter: {1}, status: {2}]", e[0], e[1], e[2])));

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

        private static bool TrySplitParameterName(string parameterName, out string name, out string modifier)
        {
            name = string.Empty;
            modifier = string.Empty;
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                return false;
            }

            string[] s = parameterName.Split(':');
            if (s.Length > 2)
            {
                return false;
            }

            name = s[0];
            if (s.Length > 1)
            {
                modifier = s[1];
            }

            return true;
        }
    }
}
