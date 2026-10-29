// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using NSubstitute;

namespace Microsoft.Health.JobManagement.UnitTests
{
    public class TestJobFactory : IJobFactory
    {
        private Func<JobInfo, IJob> _factoryFunc;

        public TestJobFactory(Func<JobInfo, IJob> factoryFunc)
        {
            _factoryFunc = factoryFunc;
        }

        public IJobScope Create(JobInfo jobInfo)
        {
            IJobScope scope = Substitute.For<IJobScope>();
            scope.Job.Returns(_factoryFunc(jobInfo));
            return scope;
        }
    }
}
