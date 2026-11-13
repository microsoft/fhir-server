// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using NSubstitute;

namespace Microsoft.Health.JobManagement.UnitTests
{
    public class TestJobMetricFactory : IJobMetricFactory
    {
        private readonly IJobMetric _jobMetric = Substitute.For<IJobMetric>();

        public IJobMetric Create(JobInfo jobInfo)
        {
            return _jobMetric;
        }
    }
}
