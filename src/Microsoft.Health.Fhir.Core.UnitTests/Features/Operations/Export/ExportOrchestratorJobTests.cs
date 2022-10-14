// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Export)]
    public class ExportOrchestratorJobTests
    {
        ISearchService _mockSearchService = Substitute.For<ISearchService>();
        IQueueClient _mockQueueClient = Substitute.For<IQueueClient>();
        ILoggerFactory _loggerFactory = new NullLoggerFactory();

        [Theory]
        public void GivenANonSystemLevelExportJob_WhenRun_ThenOneProcessingJobShouldBeCreated(string exportJobType)
        {

        }

        [Fact]
        public void GivenAnExportJobWithParallelSetToFalse_WhenRun_ThenOneProcessingJobShouldBeCreated()
        {

        }

        [Fact]
        public void GivenAnExportJobWithNoTypeRestriction_WhenRun_ThenTenProcessingJobsShouldBeCreated()
        {

        }

        [Fact]
        public void GivenAnExportJobWithTypeRestrictions_WhenRun_ThenTenProcessingJobsShouldBeCreatedPerResourceType()
        {

        }

        [Fact]
        public void GivenAnExportJobThatFails_WhenRun_ThenFailureReasonsAreInTheJobRecord()
        {

        }

        [Fact]
        public void GivenAnExportJobThatSucceeds_WhenRun_ThenOutputsAreInTheJobRecord()
        {

        }

    }
}
