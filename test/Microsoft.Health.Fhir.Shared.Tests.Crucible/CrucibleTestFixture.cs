// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Tests.Common;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Health.Fhir.Tests.E2E.Crucible
{
    public class CrucibleTestFixture : IClassFixture<CrucibleDataSource>
    {
        private readonly CrucibleDataSource _dataSource;
        private readonly ITestOutputHelper _output;

        public CrucibleTestFixture(CrucibleDataSource dataSource, ITestOutputHelper output)
        {
            _dataSource = dataSource;
            _output = output;
        }

        [Theory]
        [MemberData(nameof(GetTests))]
        [Trait(Traits.Category, Categories.Crucible)]
        public void Run(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            var findTest = _dataSource.TestRun.Value.TestRun.TestResults.FirstOrDefault(x => x.TestId == id);

            if (findTest != null)
            {
                var failures = findTest.Result
                    .Where(x =>
                    {
                        var testName = $"{x.TestId ?? findTest.TestId}/{x.Id}";
                        return x.Status == "fail" && !KnownCrucibleTests.KnownFailures.Contains(testName) && !KnownCrucibleTests.KnownBroken.Contains(testName)
                               && !x.Message.ToString().Contains(KnownCrucibleTests.BundleCountFilter);
                    })
                    .ToArray();

                if (failures.Any())
                {
                    var messages = failures
                        .Select(x =>
                            $"Failure in \"{x.TestId ?? findTest.TestId}/{x.Id}\", reason: \"{x.Message}\", description: \"{x.Description}\", see: {_dataSource.TestRun.Value.GetPermalink(x, findTest.TestId)}");

                    Assert.True(false, string.Join(Environment.NewLine, messages));
                }
                else
                {
                    var passing = findTest.Result.Where(x => x.Status == "pass").Select(x => $"{x.TestId ?? findTest.TestId}/{x.Id}").ToList();
                    if (passing.Count > 0)
                    {
                        _output.WriteLine($"Passing tests: {Environment.NewLine}{string.Join(Environment.NewLine, passing)}");
                    }

                    var failing = findTest.Result.Where(x => x.Status == "fail").Select(x => $"{x.TestId ?? findTest.TestId}/{x.Id}").ToList();
                    if (failing.Count > 0)
                    {
                        _output.WriteLine($"Excluded tests: {Environment.NewLine}{string.Join(Environment.NewLine, failing)}");
                    }
                }

                var shouldBeFailing = findTest.Result
                    .Where(x => x.Status == "pass" && KnownCrucibleTests.KnownFailures.Contains($"{x.TestId ?? findTest.TestId}/{x.Id}"))
                    .ToArray();

                if (shouldBeFailing.Any())
                {
                    var messages = shouldBeFailing
                        .Select(x =>
                            $"Previously failing test \"{x.TestId ?? findTest.TestId}/{x.Id}\" is now passing, this should be removed from known failures.");

                    Assert.True(false, string.Join(Environment.NewLine, messages));
                }
            }
        }

        public static IEnumerable<object[]> GetTests()
        {
            var client = CrucibleDataSource.CreateClient();

            if (client == null)
            {
                return Enumerable.Repeat(new object[] { null }, 1);
            }

            var ids = CrucibleDataSource.GetSupportedIdsAsync(client).GetAwaiter().GetResult();

            return ids.Select(x => new[] { x }).ToArray();
        }
    }
}
