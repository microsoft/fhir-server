// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class DateSearchTests : SearchTestsBase<DateSearchTestFixture>
    {
        public DateSearchTests(DateSearchTestFixture fixture)
            : base(fixture)
        {
        }

        // http://hl7.org/fhir/search.html#prefix
        // eq: the range of the search value has to fully contains the range of the target value.
        // ne: the range of the search value does not fully contain the range of the target value.
        // gt: the range above the search value intersects (i.e. overlaps) with the range of the target value.
        // lt: the range below the search value intersects (i.e. overlaps) with the range of the target value.
        // le: the range below the search value intersects (i.e. overlaps) with the range of the target value or the range of the search value fully contains the range of the target value.
        // ge: the range above the search value intersects (i.e. overlaps) with the range of the target value, or the range of the search value fully contains the range of the target value.
        // sa: the range of the search value does not overlap with the range of the target value, and the range above the search value contains the range of the target value.
        // eb: the range of the search value does overlap not with the range of the target value, and the range below the search value contains the range of the target value.
        [Theory]
        [InlineData("1980", 1, 2, 3, 4, 5)] // Any dates with start time greater than or equal to 1980-01-01T00:00:00.0000000 and end time less than or equal to 1980-12-31T23:59:59.9999999.
        [InlineData("1980-01")] // Any dates with start time greater than or equal to 1980-01-01T00:00:00.0000000 and end time less than or equal to 1980-01-31T23:59:59.9999999.
        [InlineData("1980-05", 2, 3, 4, 5)] // Any dates with start time greater than or equal to 1980-05-01T00:00:00.0000000 and end time less than or equal to 1980-05-31T23:59:59.9999999.
        [InlineData("1980-05-10")] // Any dates with start time greater than or equal to 1980-05-10T00:00:00.0000000 and end time less than or equal to 1980-05-10T23:59:59.9999999.
        [InlineData("1980-05-11", 3, 4, 5)] // Any dates with start time greater than or equal to 1980-05-11T00:00:00.0000000 and end time less than or equal to 1980-05-11T23:59:59.9999999.
        [InlineData("1980-05-11T16:32:15", 4, 5)] // Any dates with start time greater than or equal to 1980-05-11T16:32:15.0000000 and end time less than or equal to 1980-05-11T16:32:15.9999999.
        [InlineData("1980-05-11T16:32:15.500", 5)] // Any dates with start time greater than or equal to 1980-05-11T16:32:30.5000000 and end time less than or equal to 1980-05-11T16:32:30.50000000.
        [InlineData("1980-05-11T16:32:15.5000000", 5)] // Any dates with start time greater than or equal to 1980-05-11T16:32:30.5000000 and end time less than or equal to 1980-05-11T16:32:30.50000000.
        [InlineData("1980-05-11T16:32:15.5000001")] // Any dates with start time greater than or equal to 1980-05-11T16:32:30.50000001 and end time less than or equal to 1980-05-11T16:32:30.50000001.
        [InlineData("1980-05-11T16:32:30")] // Any dates with start time greater than or equal to 1980-05-11T16:32:30.0000000 and end time less than or equal to 1980-05-11T16:32:30.9999999.
        [InlineData("ne1980", 0, 6)] // Any dates with start time less than 1980-01-01T00:00:00.0000000 or end time greater than 1980-12-31T23:59:59.9999999.
        [InlineData("ne1980-01", 0, 1, 2, 3, 4, 5, 6)] // Any dates with start time less than 1980-01-01T00:00:00.0000000 or end time greater than 1980-01-31T23:59:59.9999999.
        [InlineData("ne1980-05", 0, 1, 6)] // Any dates with start time less than 1980-05-01T00:00:00.0000000 or end time greater than 1980-05-31T23:59:59.9999999.
        [InlineData("ne1980-05-10", 0, 1, 2, 3, 4, 5, 6)] // Any dates with start time less than 1980-05-10T00:00:00.0000000 or end time greater than 1980-05-10T23:59:59.9999999.
        [InlineData("ne1980-05-11", 0, 1, 2, 6)] // Any dates with start time less than 1980-05-11T00:00:00.0000000 or end time greater than 1980-05-11T23:59:59.9999999.
        [InlineData("ne1980-05-11T16:32:15", 0, 1, 2, 3, 6)] // Any dates with start time less than 1980-05-11T16:32:15.0000000 or end time greater than 1980-05-11T16:32:15.9999999.
        [InlineData("ne1980-05-11T16:32:15.500", 0, 1, 2, 3, 4, 6)] // Any dates with start time less than 1980-05-11T16:32:15.5000000 or end time greater than 1980-05-11T16:32:15.5000000.
        [InlineData("ne1980-05-11T16:32:15.5000000", 0, 1, 2, 3, 4, 6)] // Any dates with start time less than 1980-05-11T16:32:15.5000000 or end time greater than 1980-05-11T16:32:15.5000000.
        [InlineData("ne1980-05-11T16:32:15.5000001", 0, 1, 2, 3, 4, 5, 6)] // Any dates with start time less than 1980-05-11T16:32:15.5000001 or end time greater than 1980-05-11T16:32:15.5000001.
        [InlineData("ne1980-05-11T16:32:30", 0, 1, 2, 3, 4, 5, 6)] // Any dates with start time less than 1980-05-11T16:32:30.0000000 or end time greater than 1980-05-11T16:32:30.9999999.
        [InlineData("lt1980", 0)] // Only dates with start time earlier than 1980-01-01T00:00:00.0000000 would match.
        [InlineData("lt1980-04", 0, 1)] // Only dates with start time earlier than 1980-04-01T00:00:00.0000000 would match.
        [InlineData("lt1980-05", 0, 1)] // Only dates with start time earlier than 1980-05-01T00:00:00.0000000 would match.
        [InlineData("lt1980-05-10", 0, 1, 2)] // Only dates with start time earlier than 1980-05-10T00:00:00.0000000 would match.
        [InlineData("lt1980-05-11", 0, 1, 2)] // Only dates with start time earlier than 1980-05-11T00:00:00.0000000 would match.
        [InlineData("lt1980-05-11T16:32:14", 0, 1, 2, 3)] // Only dates with start time earlier than 1980-05-11T16:32:14.0000000 would match.
        [InlineData("lt1980-05-11T16:32:15", 0, 1, 2, 3)] // Only dates with start time earlier than 1980-05-11T16:32:15.0000000 would match.
        [InlineData("lt1980-05-11T16:32:15.4999999", 0, 1, 2, 3, 4)] // Only dates with start time earlier than 1980-05-11T16:32:15.49999999 would match.
        [InlineData("lt1980-05-11T16:32:15.500", 0, 1, 2, 3, 4)] // Only dates with start time earlier than 1980-05-11T16:32:15.5000000 would match.
        [InlineData("lt1980-05-11T16:32:15.5000000", 0, 1, 2, 3, 4)] // Only dates with start time earlier than 1980-05-11T16:32:15.5000000 would match.
        [InlineData("lt1980-05-11T16:32:15.5000001", 0, 1, 2, 3, 4, 5)] // Only dates with start time earlier than 1980-05-11T16:32:15.5000001 would match.
        [InlineData("lt1980-05-11T16:32:16", 0, 1, 2, 3, 4, 5)] // Only dates with start time earlier than 1980-05-11T16:32:16.0000000 would match.
        [InlineData("lt1980-05-12", 0, 1, 2, 3, 4, 5)] // Only dates with start time earlier than 1980-05-12T00:00:00.0000000 would match.
        [InlineData("lt1980-06", 0, 1, 2, 3, 4, 5)] // Only dates with start time earlier than 1980-06-01T00:00:00.0000000 would match.
        [InlineData("lt1981", 0, 1, 2, 3, 4, 5)] // Only dates with start time earlier than 1981-01-01T00:00:00.0000000 would match.
        [InlineData("lt1981-01-01T00:00:00.0000001", 0, 1, 2, 3, 4, 5, 6)] // Only dates with start time earlier than 1981-01-01T00:00:00.0000001 would match.
        [InlineData("gt1979-12-31T23:59:59.9999999", 1, 2, 3, 4, 5, 6)] // Only dates with end time later than 1979-12-31T23:59:59.9999999 would match.
        [InlineData("gt1980", 6)] // Only dates with end time later than 1980-12-31T23:59:59.9999999 would match.
        [InlineData("gt1980-04", 1, 2, 3, 4, 5, 6)] // Only dates with end time later than 1980-04-30T23:59:59.9999999 would match.
        [InlineData("gt1980-05", 1, 6)] // Only dates with end time later than 1980-05-31T23:59:59.9999999 would match.
        [InlineData("gt1980-05-11", 1, 2, 6)] // Only dates with end time later than 1980-05-11T23:59:59.9999999 would match.
        [InlineData("gt1980-05-11T16:32:14", 1, 2, 3, 4, 5, 6)] // Only dates with end time later than 1980-05-11T16:32:14.9999999 would match.
        [InlineData("gt1980-05-11T16:32:15", 1, 2, 3, 6)] // Only dates with end time later than 1980-05-11T16:32:15.9999999 would match.
        [InlineData("gt1980-05-11T16:32:15.4999999", 1, 2, 3, 4, 5, 6)] // Only dates with end time later than 1980-05-11T16:32:15.4999999 would match.
        [InlineData("gt1980-05-11T16:32:15.500", 1, 2, 3, 4, 6)] // Only dates with end time later than 1980-05-11T16:32:15.5000000 would match.
        [InlineData("gt1980-05-11T16:32:15.5000000", 1, 2, 3, 4, 6)] // Only dates with end time later than 1980-05-11T16:32:15.5000000 would match.
        [InlineData("gt1980-05-11T16:32:15.5000001", 1, 2, 3, 4, 6)] // Only dates with end time later than 1980-05-11T16:32:15.5000001 would match.
        [InlineData("gt1980-05-11T16:32:16", 1, 2, 3, 6)] // Only dates with end time later than 1980-05-11T16:32:16.9999999 would match.
        [InlineData("gt1980-05-12", 1, 2, 6)] // Only dates with end time later than 1980-05-12T23:59:59.9999999 would match.
        [InlineData("gt1980-06", 1, 6)] // Only dates with end time later than 1980-06-01T23:59:59.9999999 would match.
        [InlineData("gt1981-01-01T00:00:00.0000001", 6)] // Only dates with end time later than 1981-01-01T00:00:00.0000001 would match.
        [InlineData("le1980", 0, 1, 2, 3, 4, 5)] // Only dates with start time earlier than or equal to 1980-12-31T23:59:59.9999999 would match.
        [InlineData("le1980-04", 0, 1)] // Only dates with start time earlier than or equal to 1980-04-30T23:59:59.9999999 would match.
        [InlineData("le1980-05", 0, 1, 2, 3, 4, 5)] // Only dates with start time earlier than or equal to 1980-05-31T23:59:59.9999999 would match.
        [InlineData("le1980-05-10", 0, 1, 2)] // Only dates with start time earlier than or equal to 1980-05-10T23:59:59.9999999 would match.
        [InlineData("le1980-05-11", 0, 1, 2, 3, 4, 5)] // Only dates with start time earlier than or equal to 1980-05-11T23:59:59.9999999 would match.
        [InlineData("le1980-05-11T16:32:14", 0, 1, 2, 3)] // Only dates with start time earlier than or equal to 1980-05-11T16:32:14.9999999 would match.
        [InlineData("le1980-05-11T16:32:15", 0, 1, 2, 3, 4, 5)] // Only dates with start time earlier than or equal to 1980-05-11T16:32:15.9999999 would match.
        [InlineData("le1980-05-11T16:32:15.4999999", 0, 1, 2, 3, 4)] // Only dates with start time earlier than or equal to 1980-05-11T16:32:15.49999999 would match.
        [InlineData("le1980-05-11T16:32:15.500", 0, 1, 2, 3, 4, 5)] // Only dates with start time earlier than or equal to 1980-05-11T16:32:15.5000000 would match.
        [InlineData("le1980-05-11T16:32:15.5000000", 0, 1, 2, 3, 4, 5)] // Only dates with start time earlier than or equal to 1980-05-11T16:32:15.5000000 would match.
        [InlineData("le1980-05-11T16:32:15.5000001", 0, 1, 2, 3, 4, 5)] // Only dates with start time earlier than or equal to 1980-05-11T16:32:15.5000001 would match.
        [InlineData("le1980-05-11T16:32:16", 0, 1, 2, 3, 4, 5)] // Only dates with start time earlier than or equal to 1980-05-11T16:32:16.9999999 would match.
        [InlineData("le1980-05-12", 0, 1, 2, 3, 4, 5)] // Only dates with start time earlier than or equal to 1980-05-12T23:59:59.9999999 would match.
        [InlineData("le1980-06", 0, 1, 2, 3, 4, 5)] // Only dates with start time earlier than or equal to 1980-06-30T23:59:59.9999999 would match.
        [InlineData("le1981", 0, 1, 2, 3, 4, 5, 6)] // Only dates with start time earlier than or equal to 1981-12-31T23:59:59.9999999 would match.
        [InlineData("le1981-01-01T00:00:00.0000001", 0, 1, 2, 3, 4, 5, 6)] // Only dates with start time earlier than or equal to 1981-01-01T00:00:00.0000001 would match.
        [InlineData("ge1979-12-31T23:59:59.9999999", 0, 1, 2, 3, 4, 5, 6)] // Only dates with end time later than or equal to 1979-12-31T23:59:59.9999999 would match.
        [InlineData("ge1980", 1, 2, 3, 4, 5, 6)] // Only dates with end time later than or equal to 1980-01-01T00:00:00.0000000 would match.
        [InlineData("ge1980-04", 1, 2, 3, 4, 5, 6)] // Only dates with end time later than or equal to 1980-04-01T00:00:00.0000000 would match.
        [InlineData("ge1980-05", 1, 2, 3, 4, 5, 6)] // Only dates with end time later than or equal to 1980-05-01T00:00:00.0000000 would match.
        [InlineData("ge1980-05-11", 1, 2, 3, 4, 5, 6)] // Only dates with end time later than or equal to 1980-05-11T00:00:00.0000000 would match.
        [InlineData("ge1980-05-11T16:32:14", 1, 2, 3, 4, 5, 6)] // Only dates with end time later than or equal to 1980-05-11T16:32:14.0000000 would match.
        [InlineData("ge1980-05-11T16:32:15", 1, 2, 3, 4, 5, 6)] // Only dates with end time later than or equal to 1980-05-11T16:32:15.0000000 would match.
        [InlineData("ge1980-05-11T16:32:15.4999999", 1, 2, 3, 4, 5, 6)] // Only dates with end time later than or equal to 1980-05-11T16:32:15.4999999 would match.
        [InlineData("ge1980-05-11T16:32:15.500", 1, 2, 3, 4, 5, 6)] // Only dates with end time later than or equal to 1980-05-11T16:32:15.5000000 would match.
        [InlineData("ge1980-05-11T16:32:15.5000000", 1, 2, 3, 4, 5, 6)] // Only dates with end time later than or equal to 1980-05-11T16:32:15.5000000 would match.
        [InlineData("ge1980-05-11T16:32:15.5000001", 1, 2, 3, 4, 6)] // Only dates with end time later than or equal to 1980-05-11T16:32:15.5000001 would match.
        [InlineData("ge1980-05-11T16:32:16", 1, 2, 3, 6)] // Only dates with end time later than 1980-05-11T16:32:16.0000000 would match.
        [InlineData("ge1980-05-12", 1, 2, 6)] // Only dates with end time later than or equal to 1980-05-12T00:00:00.0000000 would match.
        [InlineData("ge1980-06", 1, 6)] // Only dates with end time later than or equal to 1980-06-01T00:00:00.0000000 would match.
        [InlineData("ge1981-01-01T00:00:00.0000001", 6)] // Only dates with end time later than or equal to 1981-01-01T00:00:00.0000001 would match.
        [InlineData("sa1980", 6)] // Only dates with start time later than 1981-12-31T23:59:59.9999999 would match.
        [InlineData("sa1980-04", 2, 3, 4, 5, 6)] // Only dates with start time later than 1980-04-30T23:59:59.9999999 would match.
        [InlineData("sa1980-05", 6)] // Only dates with start time later than 1980-05-31T23:59:59.9999999 would match.
        [InlineData("sa1980-05-10", 3, 4, 5, 6)] // Only dates with start time later than 1980-05-10T23:59:59.9999999 would match.
        [InlineData("sa1980-05-11", 6)] // Only dates with start time later than 1980-05-11T23:59:59.9999999 would match.
        [InlineData("sa1980-05-11T16:32:14", 4, 5, 6)] // Only dates with start time later than 1980-05-11T16:32:14.9999999 would match.
        [InlineData("sa1980-05-11T16:32:15", 6)] // Only dates with start time later than 1980-05-11T16:32:15.9999999 would match.
        [InlineData("sa1980-05-11T16:32:15.4999999", 5, 6)] // Only dates with start time later than 1980-05-11T16:32:15.49999999 would match.
        [InlineData("sa1980-05-11T16:32:15.500", 6)] // Only dates with start time later than 1980-05-11T16:32:15.5000000 would match.
        [InlineData("sa1980-05-11T16:32:15.5000000", 6)] // Only dates with start time later than 1980-05-11T16:32:15.5000000 would match.
        [InlineData("sa1980-05-11T16:32:15.5000001", 6)] // Only dates with start time later than 1980-05-11T16:32:15.5000001 would match.
        [InlineData("sa1980-05-11T16:32:16", 6)] // Only dates with start time later than 1980-05-11T16:32:16.9999999 would match.
        [InlineData("sa1980-05-12", 6)] // Only dates with start time later than 1980-05-12T23:59:59.9999999 would match.
        [InlineData("sa1980-06", 6)] // Only dates with start time later than 1980-06-30T23:59:59.9999999 would match.
        [InlineData("sa1981")] // Only dates with start time later than 1981-12-31T23:59:59.9999999 would match.
        [InlineData("sa1981-01-01T00:00:00.0000001")] // Only dates with start time later than 1981-01-01T00:00:00.0000001 would match.
        [InlineData("eb1979-12-31T23:59:59.9999999")] // Only dates with end time earlier than 1979-12-31T23:59:59.9999999 would match.
        [InlineData("eb1980", 0)] // Only dates with end time earlier than 1980-01-01T00:00:00.0000000 would match.
        [InlineData("eb1980-04", 0)] // Only dates with end time earlier than 1980-04-01T00:00:00.0000000 would match.
        [InlineData("eb1980-05", 0)] // Only dates with end time earlier than 1980-05-01T00:00:00.0000000 would match.
        [InlineData("eb1980-05-11", 0)] // Only dates with end time earlier than 1980-05-11T00:00:00.0000000 would match.
        [InlineData("eb1980-05-11T16:32:14", 0)] // Only dates with end time earlier than 1980-05-11T16:32:14.0000000 would match.
        [InlineData("eb1980-05-11T16:32:15", 0)] // Only dates with end time earlier than 1980-05-11T16:32:15.0000000 would match.
        [InlineData("eb1980-05-11T16:32:15.4999999", 0)] // Only dates with end time earlier than 1980-05-11T16:32:15.4999999 would match.
        [InlineData("eb1980-05-11T16:32:15.500", 0)] // Only dates with end time earlier than 1980-05-11T16:32:15.5000000 would match.
        [InlineData("eb1980-05-11T16:32:15.5000000", 0)] // Only dates with end time earlier than 1980-05-11T16:32:15.5000000 would match.
        [InlineData("eb1980-05-11T16:32:15.5000001", 0, 5)] // Only dates with end time earlier than 1980-05-11T16:32:15.5000001 would match.
        [InlineData("eb1980-05-11T16:32:16", 0, 4, 5)] // Only dates with end time later than 1980-05-11T16:32:16.0000000 would match.
        [InlineData("eb1980-05-12", 0, 3, 4, 5)] // Only dates with end time earlier than 1980-05-12T00:00:00.0000000 would match.
        [InlineData("eb1980-06", 0, 2, 3, 4, 5)] // Only dates with end time earlier than 1980-06-01T00:00:00.0000000 would match.
        [InlineData("eb1981-01-01T00:00:00.0000001", 0, 1, 2, 3, 4, 5)] // Only dates with end time earlier than 1981-01-01T00:00:00.0000001 would match.
        public async Task GivenADateTimeSearchParam_WhenSearched_ThenCorrectBundleShouldBeReturned(string queryValue, params int[] expectedIndices)
        {
            Bundle bundle = await Client.SearchAsync(ResourceType.Observation, $"date={queryValue}&code={Fixture.Coding.Code}");

            Observation[] expected = expectedIndices.Select(i => Fixture.Observations[i]).ToArray();

            ValidateBundle(bundle, expected);
        }

        [Theory]
        [InlineData("***")]
        [InlineData("!")]
        public async Task GivenAnInvalidDateTimeSearchParam_WhenSearched_ThenExceptionShouldBeThrown(string queryValue)
        {
            using var fhirException = await Assert.ThrowsAsync<FhirException>(async () => await Client.SearchAsync(ResourceType.Patient, $"birthdate={queryValue}"));

            Assert.Equal(HttpStatusCode.BadRequest, fhirException.StatusCode);
        }

        [Theory]
        [InlineData("1973-02-30")]
        [InlineData("1973-02-28T01:01:09.999999999999999999")]
        public async Task GivenAnOutOfRangeDateTimeSearchParam_WhenSearched_ThenExceptionShouldBeThrown(string queryValue)
        {
            using var fhirException = await Assert.ThrowsAsync<FhirException>(async () => await Client.SearchAsync(ResourceType.Patient, $"birthdate={queryValue}"));

            Assert.Equal(HttpStatusCode.BadRequest, fhirException.StatusCode);
        }
    }
}
