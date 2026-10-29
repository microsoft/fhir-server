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
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Scoping;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Api.Features.BackgroundJobService
{
    /// <summary>
    /// Factory to create different tasks.
    /// </summary>
    public class JobFactory : IJobFactory
    {
        private readonly IOwnedScopeFactory<IEnumerable<Func<IJob>>> _jobFactories;
        private readonly ILogger<JobFactory> _logger;
        private readonly Dictionary<int, int> _jobFactoryLookup;

        public JobFactory(IOwnedScopeFactory<IEnumerable<Func<IJob>>> jobFactories, ILogger<JobFactory> logger)
        {
            EnsureArg.IsNotNull(jobFactories, nameof(jobFactories));
            _jobFactories = jobFactories;
            _logger = logger;
            _jobFactoryLookup = new Dictionary<int, int>();

            using IOwnedScope<IEnumerable<Func<IJob>>> jobs = jobFactories.Create();
            foreach ((Func<IJob> Instance, int Index) jobFunc in jobs.Value.Select((lazy, i) => (Instance: lazy, Index: i)))
            {
                try
                {
                    IJob jobInstance = jobFunc.Instance.Invoke();
                    if (jobInstance.GetType().GetCustomAttribute<JobTypeIdAttribute>(false) is JobTypeIdAttribute jobTypeAttr)
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
                    logger.LogError(ex, "Failed to create job factory");
                    throw;
                }
            }
        }

        /// <inheritdoc />
        public IJobScope Create(JobInfo jobInfo)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));

            if (_jobFactoryLookup.TryGetValue(jobInfo.GetJobTypeId() ?? -1, out var index))
            {
                IOwnedScope<IEnumerable<Func<IJob>>> scope = _jobFactories.Create();
                try
                {
                    IJob instance = scope.Value.ElementAt(index).Invoke();
                    _logger.LogJobInformation(jobInfo, "Created job instance {JobInstance}", instance.GetType().Name);
                    return new ScopedJob(instance, scope);
                }
                catch
                {
                    scope.Dispose();
                    throw;
                }
            }

            throw new NotSupportedException($"Unknown task definition. ID: {jobInfo.Id}, Type: {jobInfo.GetJobTypeId()}");
        }

        private class ScopedJob(IJob value, IDisposable disposableScope) : IJobScope
        {
            private readonly IDisposable _disposableScope = EnsureArg.IsNotNull(disposableScope, nameof(disposableScope));

            public IJob Job { get; } = EnsureArg.IsNotNull(value, nameof(value));

            public void Dispose()
            {
                _disposableScope?.Dispose();
            }
        }
    }
}
