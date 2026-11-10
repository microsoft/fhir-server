// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Hl7.FhirPath;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Tests.Integration.Features.Search
{
    /// <summary>
    /// Checks reverse missing searches through production parsing, indexing, and SQL storage.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [Trait("DataStore", "SqlServer")]
    public class SqlChainedMissingStorageTests : IClassFixture<SqlChainedMissingStorageTests.MissingStorageFixture>, IAsyncLifetime
    {
        private readonly FhirStorageTestsFixture _fixture;
        private readonly IFhirRequestContext _originalRequestContext;
        private ResourceWrapperFactory _wrapperFactory;

        static SqlChainedMissingStorageTests()
        {
            FhirPathCompiler.DefaultSymbolTable.AddFhirExtensions();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlChainedMissingStorageTests"/> class.
        /// </summary>
        /// <param name="fixture">The class-scoped SQL storage fixture.</param>
        public SqlChainedMissingStorageTests(MissingStorageFixture fixture)
        {
            _fixture = fixture;
            _originalRequestContext = fixture.FhirRequestContextAccessor.RequestContext;
            fixture.FhirRequestContextAccessor.RequestContext = new FhirRequestContext(
                "PUT",
                "http://localhost/",
                "http://localhost/",
                nameof(SqlChainedMissingStorageTests),
                new Dictionary<string, StringValues>(),
                new Dictionary<string, StringValues>());
        }

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            var parser = new ReferenceSearchValueParser(_fixture.FhirRequestContextAccessor, new FhirServerInstanceConfiguration(), ResourceIdPolicy.Standard);
            var codeSystemResolver = new CodeSystemResolver(ModelInfoProvider.Instance);
            await codeSystemResolver.StartAsync(CancellationToken.None);
            var converters = typeof(ITypedElementToSearchValueConverter).Assembly.GetTypes()
                .Where(type => typeof(ITypedElementToSearchValueConverter).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface
                    && type != typeof(FhirTypedElementToSearchValueConverterManager.ExtensionConverter))
                .Select(type => (ITypedElementToSearchValueConverter)Mock.TypeWithArguments(type, parser, codeSystemResolver));
            var indexer = new TypedElementSearchIndexer(
                _fixture.SupportedSearchParameterDefinitionManager,
                new FhirTypedElementToSearchValueConverterManager(converters),
                new LightweightReferenceToElementResolver(parser, ModelInfoProvider.Instance),
                ModelInfoProvider.Instance,
                NullLogger<TypedElementSearchIndexer>.Instance);
            var compartments = new CompartmentDefinitionManager(ModelInfoProvider.Instance);
            await compartments.StartAsync(CancellationToken.None);
            var claims = Substitute.For<IClaimsExtractor>();
            claims.Extract().Returns(Array.Empty<KeyValuePair<string, string>>());
            _wrapperFactory = new ResourceWrapperFactory(
                new RawResourceFactory(new FhirJsonSerializer()),
                _fixture.FhirRequestContextAccessor,
                indexer,
                claims,
                new CompartmentIndexer(compartments),
                _fixture.SearchParameterDefinitionManager,
                _fixture.Deserializer);
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            _fixture.FhirRequestContextAccessor.RequestContext = _originalRequestContext;
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// A live missing link matches even with a present sibling, while absent and deleted links do not.
        /// </summary>
        /// <returns>The test task.</returns>
        [Fact]
        public async Task GivenPatientAndRelatedPersonBirthdates_WhenReverseSearchingMissing_ThenOnlyRootsWithLiveMissingLinksMatch()
        {
            const string prefix = "missing-";
            var deletedRoot = new Patient { Id = prefix + "deleted-root", BirthDate = "2016-07-06" };
            var patients = new[]
            {
                new Patient { Id = prefix + "missing", BirthDate = "2016-07-06" },
                new Patient { Id = prefix + "mixed", BirthDate = "2016-07-06" },
                new Patient { Id = prefix + "present", BirthDate = "2016-07-06" },
                new Patient { Id = prefix + "unlinked", BirthDate = "2016-07-06" },
                new Patient { Id = prefix + "deleted-link", BirthDate = "2016-07-06" },
                new Patient { Id = prefix + "wrong-date", BirthDate = "2017" },
                deletedRoot,
            };
            var deletedLink = new RelatedPerson { Id = prefix + "link-deleted", Patient = new ResourceReference("Patient/" + prefix + "deleted-link") };
            await UpsertResourcesAsync(patients);
            await UpsertResourcesAsync(
                new RelatedPerson { Id = prefix + "link-missing", Patient = new ResourceReference("Patient/" + prefix + "missing") },
                new RelatedPerson { Id = prefix + "link-mixed-missing", Patient = new ResourceReference("Patient/" + prefix + "mixed") },
                new RelatedPerson { Id = prefix + "link-mixed-present", Patient = new ResourceReference("Patient/" + prefix + "mixed"), BirthDate = "2016-07-06" },
                new RelatedPerson { Id = prefix + "link-present", Patient = new ResourceReference("Patient/" + prefix + "present"), BirthDate = "2016-07-06" },
                new RelatedPerson { Id = prefix + "link-deleted-root", Patient = new ResourceReference("Patient/" + deletedRoot.Id) },
                new RelatedPerson { Id = prefix + "link-wrong-date", Patient = new ResourceReference("Patient/" + prefix + "wrong-date") },
                deletedLink);
            await UpsertResourceAsync(deletedLink, deleted: true);
            await UpsertResourceAsync(deletedRoot, deleted: true);

            var result = await _fixture.SearchService.SearchAsync(
                "Patient",
                new[]
                {
                    Tuple.Create("_id", string.Join(",", patients.Select(patient => patient.Id))),
                    Tuple.Create("birthdate", "2016-07-06"),
                    Tuple.Create("_has:RelatedPerson:patient:birthdate:missing", "true"),
                },
                CancellationToken.None);

            Assert.Empty(result.UnsupportedSearchParameters);
            Assert.Empty(result.SearchIssues);
            Assert.Null(result.ContinuationToken);
            Assert.Null(result.IncludesContinuationToken);
            Assert.Equal(2, result.Results.Count());
            Assert.All(result.Results, entry => Assert.Equal(SearchEntryMode.Match, entry.SearchEntryMode));
            Assert.Equal(
                new[] { "Patient/" + prefix + "missing", "Patient/" + prefix + "mixed" }.OrderBy(id => id, StringComparer.Ordinal),
                result.Results.Select(entry => entry.Resource.ResourceTypeName + "/" + entry.Resource.ResourceId).OrderBy(id => id, StringComparer.Ordinal));
        }

        private async Task UpsertResourcesAsync(params Resource[] resources)
        {
            foreach (Resource resource in resources)
            {
                await UpsertResourceAsync(resource);
            }
        }

        private async Task UpsertResourceAsync(Resource resource, bool deleted = false)
        {
            resource.Meta = new Meta { LastUpdated = DateTimeOffset.UtcNow };
            var wrapper = _wrapperFactory.Create(resource.ToResourceElement(), deleted, keepMeta: true);
            await _fixture.DataStore.UpsertAsync(
                new ResourceWrapperOperation(wrapper, true, true, null, false, false, null),
                CancellationToken.None);
        }

        /// <summary>
        /// Uses SQL storage defaults without calendar-date classification or optimizer configuration.
        /// </summary>
        public sealed class MissingStorageFixture : FhirStorageTestsFixture
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="MissingStorageFixture"/> class.
            /// </summary>
            public MissingStorageFixture()
                : base(new SqlServerFhirStorageTestsFixture(
                    SchemaVersionConstants.Max,
                    nameof(SqlChainedMissingStorageTests) + Guid.NewGuid().ToString("N")))
            {
            }
        }
    }
}
