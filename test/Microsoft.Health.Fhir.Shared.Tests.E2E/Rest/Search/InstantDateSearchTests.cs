// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Registration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [Trait("DataStore", "SqlServer")]
    public class InstantDateSearchTests
    {
        [Theory]
        [InlineData(typeof(InstantDateStartup<Disabled, Disabled>), false, false)]
        [InlineData(typeof(InstantDateStartup<Disabled, Enabled>), false, true)]
        [InlineData(typeof(InstantDateStartup<Enabled, Disabled>), true, false)]
        [InlineData(typeof(InstantDateStartup<Enabled, Enabled>), true, true)]
        public async Task GivenAvailableSlotsForScheduledLocations_WhenSearchedOverHttp_ThenFlagVariantsReturnTheExpectedResources(Type startup, bool instant, bool containment)
        {
            Assert.SkipWhen(
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable($"{KnownEnvironmentVariableNames.TestEnvironmentUrl}{Constants.TestEnvironmentVariableVersionSqlSuffix}")),
                "This test requires an in-process SQL Server.");

            await using var server = new InProcTestFhirServer(DataStore.SqlServer, startup);
            FhirSqlServerConfiguration flags = server.Server.Services.GetRequiredService<FhirSqlServerConfiguration>();
            Assert.Equal(instant, flags.EnableInstantDateTimeRewriter);
            Assert.Equal(containment, flags.EnableFhirDateContainment);
            Assert.True(server.Server.Services.GetRequiredService<IOptions<CoreFeatureConfiguration>>().Value.EnableSearchParameterTemporalClassification);
            Assert.Equal(
                TemporalType.Instant,
                server.Server.Services.GetRequiredService<ISearchParameterDefinitionManager>().GetSearchParameter("Slot", "start").TemporalType);
            await server.ConfigureSecurityOptions();
            TestFhirClient client = server.GetTestFhirClient(ResourceFormat.Json, reusable: false);
            client.HttpClient.DefaultRequestHeaders.TryAddWithoutValidation(KnownHeaders.EnableChainSearch, "true");
            Location location = (await client.CreateAsync(new Location { Status = Location.LocationStatus.Active })).Resource;
            Location otherLocation = (await client.CreateAsync(new Location { Status = Location.LocationStatus.Active })).Resource;
            Schedule schedule = (await client.CreateAsync(new Schedule
            {
                Active = true,
                Actor = new List<ResourceReference> { new ResourceReference($"Location/{location.Id}") },
            })).Resource;
            Schedule otherSchedule = (await client.CreateAsync(new Schedule
            {
                Active = true,
                Actor = new List<ResourceReference> { new ResourceReference($"Location/{otherLocation.Id}") },
            })).Resource;
            string[] literals =
            {
                "2026-09-30T23:59:59.9999999Z",
                "2026-10-01T00:00:00Z",
                "2026-10-01T00:00:00.0000001Z",
                "2026-10-01T02:00:00+02:00",
                "2026-10-01T23:59:59.9999999Z",
                "2026-10-02T00:00:00Z",
            };
            var slots = new List<Slot>();
            foreach (string literal in literals)
            {
                DateTimeOffset start = DateTimeOffset.Parse(literal, CultureInfo.InvariantCulture);
                slots.Add((await client.CreateAsync(new Slot
                {
                    Schedule = new ResourceReference($"Schedule/{schedule.Id}"),
                    Status = Slot.SlotStatus.Free,
                    Start = start,
                    End = start.AddHours(1),
                })).Resource);
            }

            Slot wrongActor = (await client.CreateAsync(new Slot
            {
                Schedule = new ResourceReference($"Schedule/{otherSchedule.Id}"),
                Status = Slot.SlotStatus.Free,
                Start = DateTimeOffset.Parse("2026-10-01T12:00:00Z", CultureInfo.InvariantCulture),
                End = DateTimeOffset.Parse("2026-10-01T13:00:00Z", CultureInfo.InvariantCulture),
            })).Resource;
            string ids = string.Join(',', slots.Append(wrongActor).Select(s => s.Id));

            Bundle direct = await client.SearchAsync(ResourceType.Slot, $"_id={ids}&start=ge2026-10-01&start=lt2026-10-02");
            AssertIds(direct, slots.Skip(1).Take(4).Append(wrongActor).Select(s => s.Id));
            Bundle actor = await client.SearchAsync(ResourceType.Slot, $"_id={ids}&start=ge2026-10-01&start=lt2026-10-02&schedule.actor=Location/{location.Id}");
            AssertIds(actor, slots.Skip(1).Take(4).Select(s => s.Id));
            Bundle fraction = await client.SearchAsync(ResourceType.Slot, $"_id={ids}&start=eq2026-10-01T00:00:00.0000001Z");
            AssertIds(fraction, new[] { slots[2].Id });
            Bundle reverse = await client.SearchAsync(ResourceType.Schedule, $"_id={schedule.Id},{otherSchedule.Id}&_has:Slot:schedule:start=eq2026-10-01&actor=Location/{location.Id}");
            AssertIds(reverse, new[] { schedule.Id });

            Appointment matching = await CreateAppointmentAsync(client, location.Id, slots[1]);
            Appointment excluded = await CreateAppointmentAsync(client, location.Id, slots[5]);
            AppointmentResponse matchingResponse = (await client.CreateAsync(new AppointmentResponse
            {
                Appointment = new ResourceReference($"Appointment/{matching.Id}"),
                Actor = new ResourceReference($"Location/{location.Id}"),
#if R5
                ParticipantStatus = AppointmentResponse.AppointmentResponseStatus.Accepted,
#else
                ParticipantStatus = ParticipationStatus.Accepted,
#endif
            })).Resource;
            AppointmentResponse excludedResponse = (await client.CreateAsync(new AppointmentResponse
            {
                Appointment = new ResourceReference($"Appointment/{excluded.Id}"),
                Actor = new ResourceReference($"Location/{location.Id}"),
#if R5
                ParticipantStatus = AppointmentResponse.AppointmentResponseStatus.Accepted,
#else
                ParticipantStatus = ParticipationStatus.Accepted,
#endif
            })).Resource;
            Bundle forward = await client.SearchAsync(ResourceType.AppointmentResponse, $"_id={matchingResponse.Id},{excludedResponse.Id}&appointment.date=ge2026-10-01&appointment.date=lt2026-10-02");
            AssertIds(forward, new[] { matchingResponse.Id });
        }

        private static async System.Threading.Tasks.Task<Appointment> CreateAppointmentAsync(TestFhirClient client, string locationId, Slot slot) =>
            (await client.CreateAsync(new Appointment
            {
                Status = Appointment.AppointmentStatus.Booked,
                Start = slot.Start,
                End = slot.End,
                Slot = new List<ResourceReference> { new ResourceReference($"Slot/{slot.Id}") },
                Participant = new List<Appointment.ParticipantComponent>
                {
                    new Appointment.ParticipantComponent
                    {
                        Actor = new ResourceReference($"Location/{locationId}"),
#if R5
                        Status = Appointment.ParticipationStatus.Accepted,
#else
                        Status = ParticipationStatus.Accepted,
#endif
                    },
                },
            })).Resource;

        private static void AssertIds(Bundle bundle, IEnumerable<string> expected)
        {
            Assert.Equal(expected.Order(), bundle.Entry.Select(entry => entry.Resource.Id).Order());
        }

        public class Enabled
        {
        }

        public class Disabled
        {
        }

        public class InstantDateStartup<TInstant, TContainment> : StartupBaseForCustomProviders
        {
            public InstantDateStartup(IConfiguration configuration)
                : base(configuration)
            {
            }

            public override void ConfigureServices(IServiceCollection services)
            {
                Configuration["FhirServer:CoreFeatures:EnableSearchParameterTemporalClassification"] = "true";
                string connectionString = Environment.GetEnvironmentVariable(KnownEnvironmentVariableNames.SqlServerConnectionString);
                if (!string.IsNullOrEmpty(connectionString))
                {
                    Configuration["SqlServer:ConnectionString"] = connectionString;
                }

                base.ConfigureServices(services);
                services.Replace(ServiceDescriptor.Singleton(new FhirSqlServerConfiguration
                {
                    EnableInstantDateTimeRewriter = typeof(TInstant) == typeof(Enabled),
                    EnableFhirDateContainment = typeof(TContainment) == typeof(Enabled),
                }));
            }
        }
    }
}
