// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Extensions.DependencyInjection;
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

        public IScoped<IJob> Create(JobInfo jobInfo)
        {
            IScoped<IJob> scope = Substitute.For<IScoped<IJob>>();
            scope.Value.Returns(_factoryFunc(jobInfo));
            return scope;
        }
    }
}
