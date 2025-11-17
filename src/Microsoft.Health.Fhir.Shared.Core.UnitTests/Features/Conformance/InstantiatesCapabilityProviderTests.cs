// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Shared.Core.Features.Conformance;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Conformance
{
    public class InstantiatesCapabilityProviderTests
    {
        private readonly InstantiatesCapabilityProvider _provider;
        private readonly List<IInstantiateCapability> _capabilities;

        public InstantiatesCapabilityProviderTests()
        {
            _capabilities = new List<IInstantiateCapability>();
            var s = Substitute.For<IScoped<IEnumerable<IInstantiateCapability>>>();
            s.Value.Returns(_capabilities);

            _provider = new InstantiatesCapabilityProvider(() => s);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(0)]
        [InlineData(1, 0)]
        [InlineData(0, 0, 0)]
        [InlineData(1, 2, 3, 4)]
        [InlineData(1, 0, 3, 0)]
        public async Task GivenCapabilities_WhenBuilding_ThenInstantiatesFieldIsPopulated(
            params int[] counts)
        {
            var urls = AddCapabilities(counts);
            var instantiates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var builder = Substitute.For<ICapabilityStatementBuilder>();
            builder.Apply(Arg.Any<Action<ListedCapabilityStatement>>()).Returns(
                x =>
                {
                    var action = (Action<ListedCapabilityStatement>)x[0];
                    var statement = new ListedCapabilityStatement();
                    action(statement);
                    if (statement.Instantiates != null)
                    {
                        foreach (var i in statement.Instantiates)
                        {
                            instantiates.Add(i);
                        }
                    }

                    return builder;
                });

            await _provider.BuildAsync(builder, CancellationToken.None);

            Assert.Equal(urls.Count, instantiates.Count);
            Assert.All(
                urls,
                x =>
                {
                    Assert.Contains(x, instantiates);
                });
            _capabilities.ForEach(x => x.Received(1).TryGetUrls(out Arg.Any<IEnumerable<string>>()));
            builder.Received(urls.Any() ? 1 : 0).Apply(Arg.Any<Action<ListedCapabilityStatement>>());
        }

        private List<string> AddCapabilities(int[] counts)
        {
            _capabilities.Clear();
            var capabilityUrls = new List<string>();
            var i = 0;
            foreach (var count in counts)
            {
                var app = $"app{i}";
                var urls = Enumerable.Range(0, count).Select(x => $"http://hl7.org/fhir/{app}/CapabilityStatement/{x}");
                var cap = Substitute.For<IInstantiateCapability>();
                cap.TryGetUrls(out Arg.Any<IEnumerable<string>>()).Returns(
                    x =>
                    {
                        var c = count;
                        var copy = new List<string>(urls);
                        if (c > 0)
                        {
                            x[0] = copy;
                            return true;
                        }

                        x[0] = null;
                        return false;
                    });

                _capabilities.Add(cap);
                capabilityUrls.AddRange(urls);
                i++;
            }

            return capabilityUrls;
        }
    }
}
