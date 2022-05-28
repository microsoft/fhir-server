// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.JobManagement.UnitTests
{
    public class TestJobFactory : IJobFactory
    {
        private Func<JobInfo, IJob> _factoryFunc;

        public TestJobFactory(Func<JobInfo, IJob> factoryFunc)
        {
            _factoryFunc = factoryFunc;
        }

        public IJob Create(JobInfo jobInfo)
        {
            return _factoryFunc(jobInfo);
        }
    }
}
