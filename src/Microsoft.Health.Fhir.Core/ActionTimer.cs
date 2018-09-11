// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using EnsureThat;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Core
{
    public sealed class ActionTimer : IDisposable
    {
        private readonly ILogger _logger;
        private readonly string _componentName;
        private readonly Stopwatch _stopwatch;
        private readonly IDisposable _scope;

        public ActionTimer(ILogger logger, string componentName)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(componentName, nameof(componentName));

            _logger = logger;
            _componentName = componentName;
            _stopwatch = new Stopwatch();

            _scope = _logger.BeginScope($"Beginning execution of {componentName}");
            _stopwatch.Start();
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            _logger.LogInformation($"{_componentName} executed in {{Duration}}.", _stopwatch.Elapsed);
            _scope.Dispose();
        }
    }
}
