// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Api.Features.BackgroundJobService
{
    /// <summary>
    /// Factory to create different tasks.
    /// </summary>
    public class JobFactory : IJobFactory
    {
        private readonly IScopeProvider<IEnumerable<Func<IJob>>> _jobFactories;
        private readonly Dictionary<int, int> _jobFactoryLookup;

        public JobFactory(IScopeProvider<IEnumerable<Func<IJob>>> jobFactories, ILogger<JobFactory> logger)
        {
            EnsureArg.IsNotNull(jobFactories, nameof(jobFactories));
            _jobFactories = jobFactories;
            _jobFactoryLookup = new Dictionary<int, int>();

            using var jobs = jobFactories.Invoke();
            foreach (var jobFunc in jobs.Value.Select((lazy, i) => (Instance: lazy, Index: i)))
            {
                try
                {
                    var jobInstance = jobFunc.Instance.Invoke();
                    if (jobInstance.GetType().GetCustomAttribute(typeof(JobTypeIdAttribute), false) is JobTypeIdAttribute jobTypeAttr)
                    {
                        _jobFactoryLookup.Add(jobTypeAttr.JobTypeId, jobFunc.Index);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Job type {jobInstance.GetType().Name} does not have {nameof(JobTypeIdAttribute)}.");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to create job factory.");
                    throw;
                }
            }
        }

        public IScoped<IJob> Create(JobInfo jobInfo)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

            IScoped<IEnumerable<Func<IJob>>> scope = _jobFactories.Invoke();

            if (_jobFactoryLookup.TryGetValue(jobInfo.GetJobTypeId() ?? int.MinValue, out var index))
            {
                return new ScopedJob(scope.Value.ElementAt(index).Invoke(), scope);
            }

            throw new NotSupportedException($"Unknown task definition. ID: {jobInfo?.Id ?? -1}");
        }

        private class ScopedJob : IScoped<IJob>
        {
            private readonly IDisposable _disposableScope;

            public ScopedJob(IJob value, IDisposable disposableScope)
            {
                Value = EnsureArg.IsNotNull(value, nameof(value));
                _disposableScope = EnsureArg.IsNotNull(disposableScope, nameof(disposableScope));
            }

            public IJob Value { get; }

            public void Dispose()
            {
                _disposableScope?.Dispose();
            }
        }
    }
}
