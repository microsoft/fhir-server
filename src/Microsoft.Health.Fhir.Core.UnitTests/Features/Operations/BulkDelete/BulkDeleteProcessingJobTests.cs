// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.Operations.BulkDelete;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.BulkDelete
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.BulkDelete)]
    public class BulkDeleteProcessingJobTests
    {
        private IDeleter _deleter;
        private IProgress<string> _progress;
        private BulkDeleteProcessingJob _processingJob;

        public BulkDeleteProcessingJobTests()
        {
            _deleter = Substitute.For<IDeleter>();
            var deleter = Substitute.For<IScoped<IDeleter>>();
            deleter.Value.Returns(_deleter);
            _processingJob = new BulkDeleteProcessingJob(() => deleter, Substitute.For<RequestContextAccessor<IFhirRequestContext>>(), Substitute.For<IMediator>());

            _progress = new Progress<string>((result) => { });
        }

        [Fact]
        public async Task GivenProcessingJob_WhenJobIsRun_ThenResourcesAreDeleted()
        {
            _deleter.ClearReceivedCalls();

            var definition = new BulkDeleteDefinition(JobType.BulkDeleteProcessing, DeleteOperation.HardDelete, "Patient", new List<Tuple<string, string>>(), "https:\\\\test.com", "https:\\\\test.com");
            var jobInfo = new JobInfo()
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(definition),
            };

            _deleter.DeleteMultipleAsync(Arg.Any<ConditionalDeleteResourceRequest>(), Arg.Any<CancellationToken>()).Returns(args => 5);

            var result = JsonConvert.DeserializeObject<BulkDeleteResult>(await _processingJob.ExecuteAsync(jobInfo, _progress, CancellationToken.None));
            Assert.Single(result.ResourcesDeleted);
            Assert.Equal(5, result.ResourcesDeleted["Patient"]);

            await _deleter.ReceivedWithAnyArgs(1).DeleteMultipleAsync(Arg.Any<ConditionalDeleteResourceRequest>(), Arg.Any<CancellationToken>());
        }
    }
}
