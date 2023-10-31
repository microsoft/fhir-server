// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Compartment;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Operations.Import
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    public class ImportResourceParserTests
    {
        private readonly FhirJsonSerializer _jsonSerializer = new FhirJsonSerializer();

        private readonly FhirJsonParser _jsonParser = new();

        private readonly ResourceWrapperFactory _wrapperFactory;

        private readonly ImportResourceParser _importResourceParser;

        public ImportResourceParserTests()
        {
            var requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

            requestContextAccessor.RequestContext.Method.Returns("PUT");
            requestContextAccessor.RequestContext.Uri.Returns(new Uri("https://unittest/Patient/123"));

            _wrapperFactory = new ResourceWrapperFactory(
                                    new RawResourceFactory(new FhirJsonSerializer()),
                                    requestContextAccessor,
                                    Substitute.For<ISearchIndexer>(),
                                    Substitute.For<IClaimsExtractor>(),
                                    Substitute.For<ICompartmentIndexer>(),
                                    Substitute.For<ISearchParameterDefinitionManager>(),
                                    Deserializers.ResourceDeserializer);

            _importResourceParser = new(_jsonParser, _wrapperFactory);
        }

        [Fact]
        public void GivenImportWithSoftDeletedFile_WhenParsed_DeletedExtensionShouldBeRemoved()
        {
            Patient patient = new Patient();
            patient.Id = Guid.NewGuid().ToString();
            patient.Name.Add(HumanName.ForFamily("Test"));
            patient.Meta = new()
            {
                LastUpdated = DateTimeOffset.UtcNow,
                VersionId = "3",
            };
            patient.Meta.AddExtension(KnownFhirPaths.AzureSoftDeletedExtensionUrl, new FhirString("soft-deleted"));

            string patientAsString = _jsonSerializer.SerializeToString(patient);
            var importResource = _importResourceParser.Parse(0, 0, 0, patientAsString, ImportMode.IncrementalLoad);

            Assert.DoesNotContain(KnownFhirPaths.AzureSoftDeletedExtensionUrl, importResource.ResourceWrapper.RawResource.Data);
            Assert.DoesNotContain("soft-deleted", importResource.ResourceWrapper.RawResource.Data);
        }
    }
}
