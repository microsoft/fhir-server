// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using EnsureThat;
using Microsoft.Health.JobManagement;
using Microsoft.Health.TaskManagement.Exceptions;

namespace Microsoft.Health.Fhir.Api.Features.BackgroundJobService
{
    /// <summary>
    /// Factory to create different tasks.
    /// </summary>
    public class JobFactory : IJobFactory
    {
        private readonly Dictionary<int, Func<IJob>> _jobFactoryLookup;

        public JobFactory(IEnumerable<Func<IJob>> jobFactories)
        {
            EnsureArg.IsNotNull(jobFactories, nameof(jobFactories));

            _jobFactoryLookup = new Dictionary<int, Func<IJob>>();

            foreach (Func<IJob> jobFunc in jobFactories)
            {
                var instance = jobFunc.Invoke();
                if (instance.GetType().GetCustomAttribute(typeof(JobTypeIdAttribute), false) is JobTypeIdAttribute jobTypeAttr)
                {
                    _jobFactoryLookup.Add(jobTypeAttr.JobTypeId, jobFunc);
                }
                else
                {
                    throw new InvalidOperationException($"Job type {instance.GetType().Name} does not have {nameof(JobTypeIdAttribute)}.");
                }
            }
        }

        public IJob Create(JobInfo jobInfo)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

            if (_jobFactoryLookup.TryGetValue(jobInfo.GetJobTypeId() ?? int.MinValue, out Func<IJob> jobFactory))
            {
                return jobFactory.Invoke();
            }

            throw new NotSupportedException($"Unknown task definition. ID: {jobInfo?.Id ?? -1}");
        }
    }
}
