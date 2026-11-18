// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Medino;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Features.Operations.GetJobStatus
{
    /// <summary>
    /// Handler for getting a page of async job statuses.
    /// </summary>
    public class GetAllJobStatusHandler : IRequestHandler<GetAllJobStatusRequest, GetAllJobStatusResponse>
    {
        private readonly IJobStatusService _jobStatusService;
        private readonly int _pageSize;

        /// <summary>
        /// Initializes a new instance of the <see cref="GetAllJobStatusHandler"/> class.
        /// </summary>
        /// <param name="jobStatusService">The job status service.</param>
        /// <param name="operationsConfiguration">The operation configuration.</param>
        public GetAllJobStatusHandler(IJobStatusService jobStatusService, IOptions<OperationsConfiguration> operationsConfiguration)
        {
            _jobStatusService = EnsureArg.IsNotNull(jobStatusService, nameof(jobStatusService));
            EnsureArg.IsNotNull(operationsConfiguration?.Value?.JobStatus, nameof(operationsConfiguration));
            _pageSize = operationsConfiguration.Value.JobStatus.PageSize;
            if (_pageSize <= 0 || _pageSize == int.MaxValue)
            {
                throw new InvalidOperationException("Operations:JobStatus:PageSize must be between 1 and 2147483646.");
            }
        }

        /// <inheritdoc />
        public async Task<GetAllJobStatusResponse> HandleAsync(GetAllJobStatusRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            QueueType? queueType = request.JobType switch
            {
                null => null,
                OperationsConstants.Export => QueueType.Export,
                OperationsConstants.Import => QueueType.Import,
                OperationsConstants.Reindex => QueueType.Reindex,
                OperationsConstants.BulkDelete => QueueType.BulkDelete,
                OperationsConstants.BulkUpdate => QueueType.BulkUpdate,
                _ => throw new BadRequestException("jobType must be export, import, reindex, bulk-delete, or bulk-update."),
            };

            long? afterGroupId = null;
            QueueType? afterQueueType = null;
            if (request.ContinuationToken != null)
            {
                (afterGroupId, afterQueueType) = DecodeContinuationToken(request.ContinuationToken, queueType);
            }

            var jobs = await _jobStatusService.GetAllJobStatusAsync(queueType, _pageSize + 1, afterGroupId, afterQueueType, cancellationToken);
            var page = jobs.Take(_pageSize).ToArray();
            string continuationToken = null;
            if (jobs.Count > _pageSize)
            {
                var lastJob = page[page.Length - 1];
                var token = string.Format(CultureInfo.InvariantCulture, "1|{0}|{1}|{2}", (byte?)queueType ?? 0, lastJob.GroupId, (byte)lastJob.QueueType);
                continuationToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(token));
            }

            return new GetAllJobStatusResponse(page, continuationToken);
        }

        private static (long GroupId, QueueType QueueType) DecodeContinuationToken(string token, QueueType? queueType)
        {
            if (token.Length > 128)
            {
                throw new BadRequestException(Core.Resources.InvalidContinuationToken);
            }

            string decodedToken;
            try
            {
                decodedToken = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            }
            catch (FormatException)
            {
                throw new BadRequestException(Core.Resources.InvalidContinuationToken);
            }

            var parts = decodedToken.Split('|');
            if (parts.Length != 4 ||
                parts[0] != "1" ||
                !byte.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var filter) ||
                filter != ((byte?)queueType ?? 0) ||
                !long.TryParse(parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var groupId) ||
                !byte.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var lastQueueType) ||
                (QueueType)lastQueueType is not (QueueType.Export or QueueType.Import or QueueType.Reindex or QueueType.BulkDelete or QueueType.BulkUpdate) ||
                (queueType.HasValue && lastQueueType != (byte)queueType.Value))
            {
                throw new BadRequestException(Core.Resources.InvalidContinuationToken);
            }

            return (groupId, (QueueType)lastQueueType);
        }
    }
}
