// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Subscriptions.Operations
{
    [JobTypeId((int)JobType.SubscriptionsProcessing)]
    public class SubscriptionProcessingJob : IJob
    {
        public Task<string> ExecuteAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            // TODO: Write resource to channel

            return Task.FromResult("Done!");
        }
    }
}
