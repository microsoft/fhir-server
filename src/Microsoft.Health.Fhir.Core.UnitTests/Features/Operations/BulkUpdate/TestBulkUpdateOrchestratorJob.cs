// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.BulkUpdate
{
    public class TestBulkUpdateOrchestratorJob : BulkUpdateOrchestratorJob
    {
        private readonly IFhirRequestContext _testContext;

        public TestBulkUpdateOrchestratorJob(
            IQueueClient queueClient,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            Func<IScoped<ISearchService>> searchService,
            ILogger<BulkUpdateOrchestratorJob> logger,
            IFhirRequestContext testContext)
            : base(queueClient, contextAccessor, searchService, logger)
        {
            _testContext = testContext;
        }

        internal override IFhirRequestContext CreateFhirRequestContext(BulkUpdateDefinition definition, JobInfo jobInfo)
        {
            return _testContext;
        }
    }
}
