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
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Shared.Core.Features.Conformance;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Conformance
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Conformance)]
    public class InstantiatesCapabilityProviderTests
    {
        private readonly InstantiatesCapabilityProvider _provider;
        private readonly List<IInstantiateCapability> _capabilities;

        public InstantiatesCapabilityProviderTests()
        {
            _capabilities = new List<IInstantiateCapability>();
            var s = Substitute.For<IScoped<IEnumerable<IInstantiateCapability>>>();
            s.Value.Returns(_capabilities);

            _provider = new InstantiatesCapabilityProvider(
                () => s,
                Substitute.For<ILogger<InstantiatesCapabilityProvider>>());
        }

        [Theory]
        [InlineData(1)]
        [InlineData(0)]
        [InlineData(1, 0)]
        [InlineData(0, 0, 0)]
        [InlineData(1, 2, 3, 4)]
        [InlineData(1, 0, 3, 0)]
        public async Task GivenCapabilities_WhenBuilding_ThenInstantiatesFieldIsPopulatedCorrectly(
            params int[] counts)
        {
            await Run(
                counts,
                builder => _provider.BuildAsync(builder, CancellationToken.None));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(0)]
        [InlineData(1, 0)]
        [InlineData(0, 0, 0)]
        [InlineData(1, 2, 3, 4)]
        [InlineData(1, 0, 3, 0)]
        public async Task GivenCapabilities_WhenUpdating_ThenInstantiatesFieldIsUpdatedCorrectly(
            params int[] counts)
        {
            await Run(
                counts,
                builder => _provider.UpdateAsync(builder, CancellationToken.None));
        }

        private async Task Run(
            int[] counts,
            Func<ICapabilityStatementBuilder, Task> func)
        {
            var urls = AddCapabilities(counts);
            var statement = new ListedCapabilityStatement();
            var builder = Substitute.For<ICapabilityStatementBuilder>();
            builder.Apply(Arg.Any<Action<ListedCapabilityStatement>>()).Returns(
                x =>
                {
                    var action = (Action<ListedCapabilityStatement>)x[0];
                    action(statement);
                    return builder;
                });

            await func(builder);
            if (urls.Any())
            {
                Assert.NotNull(statement.Instantiates);
                Assert.Equal(urls.Count, statement.Instantiates.Count);
                Assert.All(
                    urls,
                    x =>
                    {
                        Assert.Contains(x, statement.Instantiates);
                    });
            }
            else
            {
                Assert.Null(statement.Instantiates);
            }

            _capabilities.ForEach(x => x.Received(1).GetCanonicalUrlsAsync(Arg.Any<CancellationToken>()));
            builder.Received(1).Apply(Arg.Any<Action<ListedCapabilityStatement>>());
        }

        private List<string> AddCapabilities(int[] counts)
        {
            _capabilities.Clear();
            var capabilityUrls = new List<string>();
            var i = 0;
            foreach (var count in counts)
            {
                var app = $"app{i}";
                var urls = Enumerable.Range(0, count).Select(x => $"http://hl7.org/fhir/{app}/CapabilityStatement/{x}").ToList();
                var cap = Substitute.For<IInstantiateCapability>();
                cap.GetCanonicalUrlsAsync(Arg.Any<CancellationToken>()).Returns(urls);

                _capabilities.Add(cap);
                capabilityUrls.AddRange(urls);
                i++;
            }

            return capabilityUrls;
        }
    }
}
