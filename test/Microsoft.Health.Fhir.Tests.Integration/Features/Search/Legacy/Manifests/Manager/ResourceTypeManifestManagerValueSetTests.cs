// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Xunit;
using static Microsoft.Health.Fhir.Tests.Common.Search.SearchValueValidationHelper;
using static Microsoft.Health.Fhir.Tests.Integration.Features.Search.TestHelper;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Search.Legacy.Manifests
{
    public class ResourceTypeManifestManagerValueSetTests : ResourceTypeManifestManagerTests<ValueSet>
    {
        private readonly ValueSet _valueSet = new ValueSet();

        protected override ValueSet Resource => _valueSet;

        [Fact]
        public void GivenAValueSetWithDate_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "date",
                () => { _valueSet.DateElement = new FhirDateTime(DateTime1); },
                ValidateDateTime,
                DateTime1);
        }

        [Fact]
        public void GivenAValueSetWithDescription_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "description",
                () => { _valueSet.Description = new Markdown(String1); },
                ValidateString,
                String1);
        }

        [Fact]
        public void GivenAValueSetWithExpansion_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "expansion",
                () => { _valueSet.Expansion = new ValueSet.ExpansionComponent { Identifier = Url1 }; },
                ValidateUri,
                Url1);
        }

        [Fact]
        public void GivenAValueSetWithIdentifier_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestIdentifier(v => v.Identifier);
        }

        [Fact]
        public void GivenAValueSetWithJurisdiction_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "jurisdiction",
                () =>
                {
                    _valueSet.Jurisdiction = new List<CodeableConcept>
                    {
                        CreateCodeableConcept(Coding1WithText),
                    };
                },
                Coding1WithText);
        }

        [Fact]
        public void GivenAValueSetWithName_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "name",
                () => { _valueSet.Name = String1; },
                ValidateString,
                String1);
        }

        [Fact]
        public void GivenAValueSetWithPublisher_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "publisher",
                () => { _valueSet.Publisher = String1; },
                ValidateString,
                String1);
        }

        [Fact]
        public void GivenAValueSetWithReference_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "reference",
                () =>
                {
                    _valueSet.Compose = new ValueSet.ComposeComponent
                    {
                        Include = new List<ValueSet.ConceptSetComponent>
                        {
                            new ValueSet.ConceptSetComponent
                            {
                                System = Url1,
                            },
                        },
                    };
                },
                ValidateUri,
                Url1);
        }

        [Fact]
        public void GivenAValueSetWithStatus_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestTokenSearchParam(
                "status",
                () => { _valueSet.Status = PublicationStatus.Active; },
                new Coding("http://hl7.org/fhir/publication-status", "active"));
        }

        [Fact]
        public void GivenAValueSetWithTitle_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "title",
                () => { _valueSet.Title = String1; },
                ValidateString,
                String1);
        }

        [Fact]
        public void GivenAValueSetWithUrl_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestSearchParam(
                "url",
                () => { _valueSet.Url = Url1; },
                ValidateUri,
                Url1);
        }

        [Fact]
        public void GivenAValueSetWithVersion_WhenExtracting_ThenCorrectSearchIndexEntryShouldBeCreated()
        {
            TestVersion(v => v.Version);
        }
    }
}
