// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Newtonsoft.Json;

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

        public CreateExportRequestHandler(
            IClaimsExtractor claimsExtractor,
            IFhirOperationDataStore fhirOperationDataStore,
            IAuthorizationService<DataActions> authorizationService,
            IOptions<ExportJobConfiguration> exportJobConfiguration)
        {
            EnsureArg.IsNotNull(claimsExtractor, nameof(claimsExtractor));
            EnsureArg.IsNotNull(fhirOperationDataStore, nameof(fhirOperationDataStore));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(exportJobConfiguration));

            _claimsExtractor = claimsExtractor;
            _fhirOperationDataStore = fhirOperationDataStore;
            _authorizationService = authorizationService;
            _exportJobConfiguration = exportJobConfiguration.Value;
        }

        public async Task<CreateExportResponse> Handle(CreateExportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Export, cancellationToken) != DataActions.Export)
            {
                throw new UnauthorizedFhirActionException();
            }

            IReadOnlyCollection<KeyValuePair<string, string>> requestorClaims = _claimsExtractor.Extract()?
                .OrderBy(claim => claim.Key, StringComparer.Ordinal).ToList();

            // Compute the hash of the job.
            var hashObject = new
            {
                request.RequestUri,
                RequestorClaims = requestorClaims,
            };

            string hash = JsonConvert.SerializeObject(hashObject).ComputeHash();

            string storageAccountConnectionHash = string.IsNullOrEmpty(_exportJobConfiguration.StorageAccountConnection) ?
                string.Empty :
                StringExtensions.ComputeHash(_exportJobConfiguration.StorageAccountConnection);

            // Check to see if a matching job exists or not. If a matching job exists, we will return that instead.
            // Otherwise, we will create a new export job. This will be a best effort since the likelihood of this happen should be small.
            ExportJobOutcome outcome = await _fhirOperationDataStore.GetExportJobByHashAsync(hash, cancellationToken);

            var filters = ParseFilter(request.Filters);

            ExportJobFormatConfiguration formatConfiguration = ParseFormat(request.FormatName, request.ContainerName != null);

            if (outcome == null)
            {
                var jobRecord = new ExportJobRecord(
                    request.RequestUri,
                    request.RequestType,
                    formatConfiguration.Format,
                    request.ResourceType,
                    filters,
                    hash,
                    _exportJobConfiguration.RollingFileSizeInMB,
                    requestorClaims,
                    request.Since,
                    request.GroupId,
                    storageAccountConnectionHash,
                    _exportJobConfiguration.StorageAccountUri,
                    request.AnonymizationConfigurationLocation,
                    request.AnonymizationConfigurationFileETag,
                    _exportJobConfiguration.MaximumNumberOfResourcesPerQuery,
                    _exportJobConfiguration.NumberOfPagesPerCommit,
                    request.ContainerName);

                outcome = await _fhirOperationDataStore.CreateExportJobAsync(jobRecord, cancellationToken);
            }

            return new CreateExportResponse(outcome.JobRecord.Id);
        }

        /// <summary>
        /// Parses the _typeFilter parameter from a string into a list of <see cref="ExportJobFilter"/> objects.
        /// </summary>
        /// <param name="filterString">The _typeFilter parameter input.</param>
        /// <returns>A list of <see cref="ExportJobFilter"/></returns>
        private static IList<ExportJobFilter> ParseFilter(string filterString)
        {
            var filters = new List<ExportJobFilter>();

            if (!string.IsNullOrWhiteSpace(filterString))
            {
                var filterArray = filterString.Split(",");
                foreach (string filter in filterArray)
                {
                    var parameterIndex = filter.IndexOf("?", StringComparison.Ordinal);

                    if (parameterIndex < 0 || parameterIndex == filter.Length - 1)
                    {
                        throw new BadRequestException(string.Format(Resources.TypeFilterUnparseable, filter));
                    }

                    var filterType = filter.Substring(0, parameterIndex);

                    var filterParameters = filter.Substring(parameterIndex + 1).Split("&");
                    var parameterTupleList = new List<Tuple<string, string>>();

                    foreach (string parameter in filterParameters)
                    {
                        var keyValue = parameter.Split("=");

                        if (keyValue.Length != 2)
                        {
                            throw new BadRequestException(string.Format(Resources.TypeFilterUnparseable, filter));
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
                    throw new BadRequestException(string.Format(Resources.ExportFormatNotFound, formatName));
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
    }
}
