// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Hl7.Fhir.Utility;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using NSubstitute;
using Xunit;
using static Hl7.Fhir.Model.Observation;
using static Microsoft.Health.Fhir.Tests.Common.Search.SearchValueValidationHelper;
using static Microsoft.Health.Fhir.Tests.Integration.Features.Search.TestHelper;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Search.Legacy.Manifests
{
    public class ResourceTypeManifestBuilderTests
    {
        private const string ParamName = "param";

        private readonly ISearchParamDefinitionManager _searchParamDefinitionManager = Substitute.For<ISearchParamDefinitionManager>();
        private readonly SearchParamFactory _searchParamFactory;
        private readonly ResourceTypeManifestBuilder<TestResource> _resourceTypeManifestBuilder;
        private readonly ICollection<SearchParamValidator> genericParamValidators = new List<SearchParamValidator>
        {
            new SearchParamValidator(SearchParamNames.Id, GenerateTokenSearchParamValidator(String1)),
            new SearchParamValidator(SearchParamNames.LastUpdated, GenerateDateTimeSearchParamValidator(DateTime1)),
            new SearchParamValidator(SearchParamNames.Profile, GenerateUriSearchParamValidator(Url1)),
            new SearchParamValidator(SearchParamNames.Security, GenerateTokenSearchParamValidator(String1)),
            new SearchParamValidator(SearchParamNames.Tag, GenerateTokenSearchParamValidator(String1)),
        };

        public ResourceTypeManifestBuilderTests()
        {
            _searchParamFactory = new SearchParamFactory(_searchParamDefinitionManager);
            _resourceTypeManifestBuilder = new ResourceTypeManifestBuilder<TestResource>(_searchParamFactory);
            SetupGenericSearchParams();
        }

        [Fact]
        public void GivenACompositeDateTimeSearchParam_WhenAdded_ThenCorrectSearchParamIsReturned()
        {
            SetupCompositeDateTimeSearchParam(ParamName, Coding1WithText, DateTime1);

            ValidateSearchParam(
                new CompositeSearchParamValidator(
                    ParamName,
                    SearchParamType.Date,
                    GenerateCompositeDateTimeSearchParamValidator(Coding1WithText, DateTime1)));
        }

        [Fact]
        public void GivenMultipleCompositeDateTimeSearchParamsWithSameParamName_WhenAdded_ThenCorrectSearchParamIsReturned()
        {
            SetupCompositeDateTimeSearchParam(ParamName, Coding1WithText, DateTime1);
            SetupCompositeDateTimeSearchParam(ParamName, Coding2, DateTime2);

            ValidateSearchParam(
                new CompositeSearchParamValidator(
                    ParamName,
                    SearchParamType.Date,
                    GenerateCompositeDateTimeSearchParamValidator(Coding1WithText, DateTime1),
                    GenerateCompositeDateTimeSearchParamValidator(Coding2, DateTime2)));
        }

        [Fact]
        public void GivenACompositeQuantitySearchParam_WhenAdded_ThenCorrectSearchParamIsReturned()
        {
            SetupCompositeQuantitySearchParam(ParamName, Coding1WithText, Quantity1);

            ValidateSearchParam(
                new CompositeSearchParamValidator(
                    ParamName,
                    SearchParamType.Quantity,
                    GenerateCompositeQuantitySearchParamValidator(Coding1WithText, Quantity1)));
        }

        [Fact]
        public void GivenMultipleCompositeQuantitySearchParamsWithSameParamName_WhenAdded_ThenCorrectSearchParamIsReturned()
        {
            SetupCompositeQuantitySearchParam(ParamName, Coding1WithText, Quantity1);
            SetupCompositeQuantitySearchParam(ParamName, Coding2, Quantity2);

            ValidateSearchParam(
                new CompositeSearchParamValidator(
                    ParamName,
                    SearchParamType.Quantity,
                    GenerateCompositeQuantitySearchParamValidator(Coding1WithText, Quantity1),
                    GenerateCompositeQuantitySearchParamValidator(Coding2, Quantity2)));
        }

        [Fact]
        public void GivenACompositeReferenceSearchParam_WhenAdded_ThenCorrectSearchParamIsReturned()
        {
            ObservationRelationshipType relationshipType = ObservationRelationshipType.QualifiedBy;

            SetupCompositeReferenceSearchParam(ParamName, relationshipType, PatientReference);

            ValidateSearchParam(
                new CompositeSearchParamValidator(
                    ParamName,
                    SearchParamType.Reference,
                    GenerateCompositeReferenceSearchParamValidator(relationshipType, PatientReference)));
        }

        [Fact]
        public void GivenMultipleCompositeReferenceSearchParamsWithSameParamName_WhenAdded_ThenCorrectSearchParamIsReturned()
        {
            ObservationRelationshipType relationshipType1 = ObservationRelationshipType.QualifiedBy;

            SetupCompositeReferenceSearchParam(ParamName, relationshipType1, PatientReference);

            ObservationRelationshipType relationshipType2 = ObservationRelationshipType.HasMember;

            SetupCompositeReferenceSearchParam(ParamName, relationshipType2, OrganizationReference);

            ValidateSearchParam(
                new CompositeSearchParamValidator(
                    ParamName,
                    SearchParamType.Reference,
                    GenerateCompositeReferenceSearchParamValidator(relationshipType1, PatientReference),
                    GenerateCompositeReferenceSearchParamValidator(relationshipType2, OrganizationReference)));
        }

        [Fact]
        public void GivenACompositeStringSearchParam_WhenAdded_ThenCorrectSearchParamIsReturned()
        {
            string inputString = "test";

            SetupCompositeStringSearchParam(ParamName, Coding1WithText, inputString);

            ValidateSearchParam(
                new CompositeSearchParamValidator(
                    ParamName,
                    SearchParamType.String,
                    GenerateCompositeStringSearchParamValidator(Coding1WithText, inputString)));
        }

        [Fact]
        public void GivenMultipleCompositeStringSearchParamsWithSameName_WhenAdded_ThenCorrectSearchParamIsReturned()
        {
            string inputString1 = "test1";

            SetupCompositeStringSearchParam(ParamName, Coding1WithText, inputString1);

            string inputString2 = "manifest";

            SetupCompositeStringSearchParam(ParamName, Coding2, inputString2);

            ValidateSearchParam(
                new CompositeSearchParamValidator(
                    ParamName,
                    SearchParamType.String,
                    GenerateCompositeStringSearchParamValidator(Coding1WithText, inputString1),
                    GenerateCompositeStringSearchParamValidator(Coding2, inputString2)));
        }

        [Fact]
        public void GivenACompositeTokenSearchParam_WhenAdded_ThenCorrectSearchParamIsReturned()
        {
            SetupCompositeTokenSearchParam(ParamName, Coding1WithText, Coding2);

            ValidateSearchParam(
                new CompositeSearchParamValidator(
                    ParamName,
                    SearchParamType.Token,
                    GenerateCompositeTokenSearchParamValidator(Coding1WithText, Coding2)));
        }

        [Fact]
        public void GivenMultipleCompositeTokenSearchParamsWithSameParamName_WhenAdded_ThenCorrectSearchParamIsReturned()
        {
            SetupCompositeTokenSearchParam(ParamName, Coding1WithText, Coding2);
            SetupCompositeTokenSearchParam(ParamName, Coding2, Coding1WithText);

            ValidateSearchParam(
                new CompositeSearchParamValidator(
                    ParamName,
                    SearchParamType.Token,
                    GenerateCompositeTokenSearchParamValidator(Coding1WithText, Coding2),
                    GenerateCompositeTokenSearchParamValidator(Coding2, Coding1WithText)));
        }

        [Fact]
        public void GivenADateTimeSearchParam_WhenAdded_ThenCorrectSearchParamIsReturned()
        {
            SetupDateTimeSearchParam(ParamName, DateTime1);

            ValidateSearchParam(
                new SearchParamValidator(
                    ParamName,
                    GenerateDateTimeSearchParamValidator(DateTime1)));
        }

        [Fact]
        public void GivenMultipleDateTimeSearchParamsWithSameSearchParamName_WhenAdded_ThenCorrectSearchParamIsReturned()
        {
            SetupDateTimeSearchParam(ParamName, DateTime1);
            SetupDateTimeSearchParam(ParamName, DateTime2);

            ValidateSearchParam(
                new SearchParamValidator(
                    ParamName,
                    GenerateDateTimeSearchParamValidator(DateTime1),
                    GenerateDateTimeSearchParamValidator(DateTime2)));
        }

        [Fact]
        public void GivenANumberSearchParam_WhenAdded_ThenCorrectSearchParamIsReturned()
        {
            SetupNumberSearchParam(ParamName, Number1);

            ValidateSearchParam(
                new SearchParamValidator(
                    ParamName,
                    GenerateNumberSearchParamValidator(Number1)));
        }

        [Fact]
        public void GivenMultipleNumberSearchParamsWithSameSearchParamName_WhenAdded_ThenCorrectSearchParamIsReturned()
        {
            SetupNumberSearchParam(ParamName, Number1);
            SetupNumberSearchParam(ParamName, Number2);

            ValidateSearchParam(
                new SearchParamValidator(
                    ParamName,
                    GenerateNumberSearchParamValidator(Number1),
                    GenerateNumberSearchParamValidator(Number2)));
        }

        [Fact]
        public void GivenAQuantitySearchParam_WhenAdded_ThenCorrectSearchParamIsReturned()
        {
            SetupQuantitySearchParam(ParamName, Quantity1);

            ValidateSearchParam(
                new SearchParamValidator(
                    ParamName,
                    GenerateQuantitySearchParamValidator(Quantity1)));
        }

        [Fact]
        public void GivenMultipleQuantitySearchParamsWithSameParamName_WhenAdded_ThenCorrectSearchParamIsReturned()
        {
            SetupQuantitySearchParam(ParamName, Quantity1);
            SetupQuantitySearchParam(ParamName, Quantity2);

            ValidateSearchParam(
                new SearchParamValidator(
                    ParamName,
                    GenerateQuantitySearchParamValidator(Quantity1),
                    GenerateQuantitySearchParamValidator(Quantity2)));
        }

        [Fact]
        public void GivenAReferenceSearchParam_WhenAdded_ThenCorrectSearchParamIsReturned()
        {
            SetupReferenceSearchParam(ParamName, PatientReference);

            ValidateSearchParam(
                new SearchParamValidator(
                    ParamName,
                    GenerateReferenceSearchParamValidator(PatientReference)));
        }

        [Fact]
        public void GivenMultipleReferenceSearchParamsWithSameParamName_WhenAdded_ThenCorrectSearchParamIsReturned()
        {
            SetupReferenceSearchParam(ParamName, PatientReference);
            SetupReferenceSearchParam(ParamName, OrganizationReference);

            ValidateSearchParam(
                new SearchParamValidator(
                    ParamName,
                    GenerateReferenceSearchParamValidator(PatientReference),
                    GenerateReferenceSearchParamValidator(OrganizationReference)));
        }

        [Fact]
        public void GivenAStringSearchParam_WhenAdded_ThenCorrectSearchParamIsReturned()
        {
            SetupStringSearchParam(ParamName, String1);

            ValidateSearchParam(
                new SearchParamValidator(
                    ParamName,
                    GenerateStringSearchParamValidator(String1)));
        }

        [Fact]
        public void GivenMultipleStringSearchParamsWithSameParamName_WhenAdded_ThenCorrectSearchParamIsReturned()
        {
            SetupStringSearchParam(ParamName, String1);
            SetupStringSearchParam(ParamName, String2);

            ValidateSearchParam(
                new SearchParamValidator(
                    ParamName,
                    GenerateStringSearchParamValidator(String1),
                    GenerateStringSearchParamValidator(String2)));
        }

        [Fact]
        public void GivenATokenSearchParam_WhenAdded_ThenCorrectSearchParamIsReturned()
        {
            SetupTokenSearchParam(ParamName, Coding1WithText);

            ValidateSearchParam(
                new SearchParamValidator(
                    ParamName,
                    GenerateTokenSearchParamValidator(Coding1WithText)));
        }

        [Fact]
        public void GivenMultipleTokenSearchParamsWithSameParamName_WhenAdded_ThenCorrectSearchParamIsReturned()
        {
            SetupTokenSearchParam(ParamName, Coding1WithText);
            SetupTokenSearchParam(ParamName, Coding2);

            ValidateSearchParam(
                new SearchParamValidator(
                    ParamName,
                    GenerateTokenSearchParamValidator(Coding1WithText),
                    GenerateTokenSearchParamValidator(Coding2)));
        }

        [Fact]
        public void GivenAUriSearchParam_WhenAdded_ThenCorrectSearchParamIsReturned()
        {
            SetupUriSearchParam(ParamName, Url1);

            ValidateSearchParam(
                new SearchParamValidator(
                    ParamName,
                    GenerateUriSearchParamValidator(Url1)));
        }

        [Fact]
        public void GivenMultipleUriSearchParamsWithSameName_WhenAdded_ThenCorrectSearchParamIsReturned()
        {
            SetupUriSearchParam(ParamName, Url1);
            SetupUriSearchParam(ParamName, Url2);

            ValidateSearchParam(
                new SearchParamValidator(
                    ParamName,
                    GenerateUriSearchParamValidator(Url1),
                    GenerateUriSearchParamValidator(Url2)));
        }

        [Fact]
        public void GivenMultipleSearchParam_WhenAdded_ThenCorrectSearchParamsAreReturned()
        {
            SetupCompositeReferenceSearchParam("target", ObservationRelationshipType.Replaces, PatientReference);
            SetupCompositeQuantitySearchParam("code-quantity", Coding1WithText, Quantity1);
            SetupCompositeDateTimeSearchParam("code-date", Coding1WithText, DateTime1);
            SetupCompositeStringSearchParam("comment", Coding2, String1);
            SetupCompositeStringSearchParam("comment", Coding1WithText, String2);
            SetupCompositeTokenSearchParam("duplicate", Coding1WithText, Coding2);
            SetupDateTimeSearchParam("date", DateTime2);
            SetupQuantitySearchParam("quantity", Quantity2);
            SetupReferenceSearchParam("context", OrganizationReference);
            SetupStringSearchParam("version", String1);
            SetupTokenSearchParam("code", Coding2);
            SetupUriSearchParam("url", Url1);

            ValidateSearchParam(
                new SearchParamValidator("code", GenerateTokenSearchParamValidator(Coding2)),
                new CompositeSearchParamValidator("code-date", SearchParamType.Date, GenerateCompositeDateTimeSearchParamValidator(Coding1WithText, DateTime1)),
                new CompositeSearchParamValidator("code-quantity", SearchParamType.Quantity, GenerateCompositeQuantitySearchParamValidator(Coding1WithText, Quantity1)),
                new CompositeSearchParamValidator("comment", SearchParamType.String, GenerateCompositeStringSearchParamValidator(Coding2, String1), GenerateCompositeStringSearchParamValidator(Coding1WithText, String2)),
                new SearchParamValidator("context", GenerateReferenceSearchParamValidator(OrganizationReference)),
                new SearchParamValidator("date", GenerateDateTimeSearchParamValidator(DateTime2)),
                new CompositeSearchParamValidator("duplicate", SearchParamType.Token, GenerateCompositeTokenSearchParamValidator(Coding1WithText, Coding2)),
                new SearchParamValidator("quantity", GenerateQuantitySearchParamValidator(Quantity2)),
                new CompositeSearchParamValidator("target", SearchParamType.Reference, GenerateCompositeReferenceSearchParamValidator(ObservationRelationshipType.Replaces, PatientReference)),
                new SearchParamValidator("url", GenerateUriSearchParamValidator(Url1)),
                new SearchParamValidator("version", GenerateStringSearchParamValidator(String1)));
        }

        private static SearchValueValidator CreateSearchValueValidator<TValue>(
                TValue expectedValue,
                Func<TValue, string> inputStringGenerator,
                Action<TValue, ISearchValue> searchValueValidator)
        {
            string inputStringToBeParsed = inputStringGenerator(expectedValue);
            Action<ISearchValue> validator = sv => searchValueValidator(expectedValue, sv);

            return new SearchValueValidator(
                inputStringToBeParsed,
                validator);
        }

        private static CompositeSearchValueValidator CreateSearchValueValidator<TValue>(
                Coding expectedCoding,
                TValue expectedValue,
                Func<TValue, string> inputStringGenerator,
                Action<TValue, ISearchValue> searchValueValidator)
        {
            string inputStringToBeParsed =
                $"{SearchValueStringGenerator.GenerateTokenString(expectedCoding)}${inputStringGenerator(expectedValue)}";

            Action<ISearchValue> validator = sv => searchValueValidator(expectedValue, sv);

            return new CompositeSearchValueValidator(
                expectedCoding,
                inputStringToBeParsed,
                validator);
        }

        private static SearchValueValidator GenerateDateTimeSearchParamValidator(
            string expectedDateTime) =>
            CreateSearchValueValidator(
                expectedDateTime,
                SearchValueStringGenerator.GenerateString,
                ValidateDateTime);

        private static SearchValueValidator GenerateNumberSearchParamValidator(
            decimal expectedNumber) =>
            CreateSearchValueValidator(
                expectedNumber,
                SearchValueStringGenerator.GenerateNumber,
                ValidateNumber);

        private static SearchValueValidator GenerateQuantitySearchParamValidator(
            Quantity expectedQuantity) =>
            CreateSearchValueValidator(
                expectedQuantity,
                SearchValueStringGenerator.GenerateQuantityString,
                ValidateQuantity);

        private static SearchValueValidator GenerateReferenceSearchParamValidator(
            string expectedReference) =>
            CreateSearchValueValidator(
                expectedReference,
                SearchValueStringGenerator.GenerateString,
                ValidateReference);

        private static SearchValueValidator GenerateStringSearchParamValidator(
            string expectedString) =>
            CreateSearchValueValidator(
                expectedString,
                SearchValueStringGenerator.GenerateString,
                ValidateString);

        private static SearchValueValidator GenerateTokenSearchParamValidator(
            Coding expectedTokenCoding) =>
            CreateSearchValueValidator(
                expectedTokenCoding,
                SearchValueStringGenerator.GenerateTokenString,
                ValidateToken);

        private static SearchValueValidator GenerateTokenSearchParamValidator(
            string expectedTokenString) =>
            CreateSearchValueValidator(
                expectedTokenString,
                SearchValueStringGenerator.GenerateString,
                ValidateTokenString);

        private static SearchValueValidator GenerateUriSearchParamValidator(
            string expectedUrl) =>
            CreateSearchValueValidator(
                expectedUrl,
                SearchValueStringGenerator.GenerateString,
                ValidateUri);

        private static CompositeSearchValueValidator GenerateCompositeDateTimeSearchParamValidator(
            Coding expectedCoding,
            string expectedDateTime) =>
            CreateSearchValueValidator(
                expectedCoding,
                expectedDateTime,
                SearchValueStringGenerator.GenerateString,
                ValidateDateTime);

        private static CompositeSearchValueValidator GenerateCompositeQuantitySearchParamValidator(
            Coding expectedCoding,
            Quantity expectedQuantity) =>
            CreateSearchValueValidator(
                expectedCoding,
                expectedQuantity,
                SearchValueStringGenerator.GenerateQuantityString,
                ValidateQuantity);

        private static CompositeSearchValueValidator GenerateCompositeReferenceSearchParamValidator(
            Enum expectedEnumValue,
            string expectedReference)
        {
            Coding expectedCoding = new Coding(expectedEnumValue.GetSystem(), expectedEnumValue.GetLiteral());

            return CreateSearchValueValidator(
                expectedCoding,
                expectedReference,
                SearchValueStringGenerator.GenerateString,
                ValidateReference);
        }

        private static CompositeSearchValueValidator GenerateCompositeStringSearchParamValidator(
            Coding expectedCoding,
            string expectedString) =>
            CreateSearchValueValidator(
                expectedCoding,
                expectedString,
                SearchValueStringGenerator.GenerateString,
                ValidateString);

        private static CompositeSearchValueValidator GenerateCompositeTokenSearchParamValidator(
            Coding expectedCompositeCoding,
            Coding expectedTokenCoding) =>
            CreateSearchValueValidator(
                expectedCompositeCoding,
                expectedTokenCoding,
                SearchValueStringGenerator.GenerateTokenString,
                ValidateToken);

        private void SetupGenericSearchParams()
        {
            SetupTokenSearchParam(SearchParamNames.Id, String1);
            SetupDateTimeSearchParam(SearchParamNames.LastUpdated, DateTime1);
            SetupUriSearchParam(SearchParamNames.Profile, Url1);
            SetupTokenSearchParam(SearchParamNames.Security, String1);
            SetupTokenSearchParam(SearchParamNames.Tag, String1);
        }

        private void SetupDateTimeSearchParam(string paramName, string dateTime)
        {
            var extractor = new DateTimeExtractor<TestResource, string>(
                _ => Enumerable.Repeat(dateTime, 1),
                s => s,
                e => e);

            _resourceTypeManifestBuilder.AddDateTimeSearchParam(paramName, extractor);
        }

        private void SetupNumberSearchParam(string paramName, decimal? number)
        {
            var extractor = new NumberExtractor<TestResource>(
                _ => Enumerable.Repeat(number, 1));

            _resourceTypeManifestBuilder.AddNumberSearchParam(paramName, extractor);
        }

        private void SetupQuantitySearchParam(string paramName, Quantity quantity)
        {
            var extractor = new QuantityExtractor<TestResource, TestResource>(
                r => Enumerable.Repeat(r, 1),
                _ => quantity);

            _resourceTypeManifestBuilder.AddQuantitySearchParam(paramName, extractor);
        }

        private void SetupReferenceSearchParam(string paramName, string reference)
        {
            var extractor = new ReferenceExtractor<TestResource, TestResource>(
                r => Enumerable.Repeat(r, 1),
                _ => new ResourceReference(reference));

            _resourceTypeManifestBuilder.AddReferenceSearchParam(paramName, extractor);
        }

        private void SetupStringSearchParam(string paramName, string stringValue)
        {
            var extractor = new StringExtractor<TestResource>(
                            _ => Enumerable.Repeat(stringValue, 1));

            _resourceTypeManifestBuilder.AddStringSearchParam(paramName, extractor);
        }

        private void SetupTokenSearchParam(string paramName, Coding coding)
        {
            var extractor = new TokenExtractor<TestResource, TestResource>(
                r => Enumerable.Repeat(r, 1),
                _ => coding.System,
                _ => coding.Code,
                _ => coding.Display);

            _resourceTypeManifestBuilder.AddTokenSearchParam(paramName, extractor);
        }

        private void SetupTokenSearchParam(string paramName, string stringValue)
        {
            var extractor = new TokenExtractor<TestResource, string>(
                resource => Enumerable.Repeat(stringValue, 1),
                s => null,
                s => s);

            _resourceTypeManifestBuilder.AddTokenSearchParam(paramName, extractor);
        }

        private void SetupUriSearchParam(string paramName, string url)
        {
            var extractor = new UriExtractor<TestResource>(
                _ => Enumerable.Repeat(url, 1));

            _resourceTypeManifestBuilder.AddUriSearchParam(paramName, extractor);
        }

        private void SetupCompositeDateTimeSearchParam(string paramName, Coding coding, string dateTime)
        {
            var extractor = new CompositeDateTimeExtractor<TestResource>(
                _ => CreateCodeableConcept(coding),
                _ => new FhirDateTime(dateTime));

            _resourceTypeManifestBuilder.AddCompositeDateTimeSearchParam(paramName, extractor);
        }

        private void SetupCompositeQuantitySearchParam(string paramName, Coding coding, Quantity quantity)
        {
            var component = new[]
            {
                new ComponentComponent()
                {
                    Code = new CodeableConcept(coding.System, coding.Code),
                    Value = quantity,
                },
            };

            var extractor = new CompositeQuantityExtractor<TestResource, ComponentComponent>(
                r => component,
                c => c.Code,
                c => c.Value as Quantity);

            _resourceTypeManifestBuilder.AddCompositeQuantitySearchParam(paramName, extractor);
        }

        private void SetupCompositeReferenceSearchParam(string paramName, ObservationRelationshipType relationshipType, string reference)
        {
            var component = new[]
            {
                new RelatedComponent()
                {
                    Type = relationshipType,
                    Target = new ResourceReference(reference),
                },
            };

            var extractor = new CompositeReferenceExtractor<TestResource, RelatedComponent>(
                r => component,
                r => r.Type,
                r => r.Target);

            _resourceTypeManifestBuilder.AddCompositeReferenceSearchParam(paramName, extractor);
        }

        private void SetupCompositeStringSearchParam(string paramName, Coding coding, string stringValue)
        {
            var extractor = new CompositeStringExtractor<TestResource>(
                _ => CreateCodeableConcept(coding),
                _ => new FhirString(stringValue));

            _resourceTypeManifestBuilder.AddCompositeStringSearchParam(paramName, extractor);
        }

        private void SetupCompositeTokenSearchParam(string paramName, Coding compositeCoding, Coding tokenCoding)
        {
            var extractor = new CompositeTokenExtractor<TestResource, TestResource>(
                r => Enumerable.Repeat(r, 1),
                _ => CreateCodeableConcept(compositeCoding),
                _ => CreateCodeableConcept(tokenCoding));

            _resourceTypeManifestBuilder.AddCompositeTokenSearchParam(paramName, extractor);
        }

        private void ValidateSearchParam(
            params SearchParamValidator[] validators)
        {
            ResourceTypeManifest manifest = _resourceTypeManifestBuilder.ToManifest();

            Assert.NotNull(manifest);
            Assert.Equal(typeof(TestResource), manifest.ResourceType);

            Action<SearchParam>[] supportedParamInspectors = genericParamValidators.Concat(validators).Select(spv =>
            {
                return new Action<SearchParam>(sp =>
                {
                    Assert.Equal(spv.ParamName, sp.ParamName);
                    Assert.Equal(typeof(TestResource), sp.ResourceType);

                    if (spv is CompositeSearchParamValidator csvv)
                    {
                        CompositeSearchParam csp = Assert.IsType<CompositeSearchParam>(sp);

                        Assert.Equal(csvv.UnderlyingSearchParamType, csp.UnderlyingSearchParamType);
                    }

                    IEnumerable<ISearchValue> extractedValues = sp.ExtractValues(new TestResource());

                    Assert.NotNull(extractedValues);

                    Action<ISearchValue>[] searchValueInspectors = spv.Validators.Select(
                        svv =>
                        {
                            return new Action<ISearchValue>(sv =>
                            {
                                // Validate the search value from extraction.
                                ValidateSearchValue(svv, sv);

                                // Validate the search value from parsing.
                                ValidateSearchValue(svv, sp.Parse(svv.InputToBeParsed));
                            });
                        }).ToArray();

                    Assert.Collection(
                        extractedValues,
                        searchValueInspectors);
                });
            }).ToArray();

            Assert.Collection(
                manifest.SupportedSearchParams,
                supportedParamInspectors);

            void ValidateSearchValue(SearchValueValidator searchValueValidator, ISearchValue sv)
            {
                if (searchValueValidator is CompositeSearchValueValidator csvv)
                {
                    LegacyCompositeSearchValue csv = Assert.IsType<LegacyCompositeSearchValue>(sv);

                    Assert.Equal(csvv.ExpectedCoding.System, csv.System);
                    Assert.Equal(csvv.ExpectedCoding.Code, csv.Code);

                    searchValueValidator.Validator(csv.Value);
                }
                else
                {
                    searchValueValidator.Validator(sv);
                }
            }
        }

        private class SearchParamValidator
        {
            public SearchParamValidator(
                string paramName,
                params SearchValueValidator[] validators)
            {
                ParamName = paramName;
                Validators = validators;
            }

            public string ParamName { get; }

            public IReadOnlyCollection<SearchValueValidator> Validators { get; }
        }

        private class CompositeSearchParamValidator
            : SearchParamValidator
        {
            public CompositeSearchParamValidator(
                string paramName,
                SearchParamType underlyingSearchParamType,
                params CompositeSearchValueValidator[] validators)
                : base(paramName, validators)
            {
                UnderlyingSearchParamType = underlyingSearchParamType;
            }

            public SearchParamType UnderlyingSearchParamType { get; }
        }

        private class SearchValueValidator
        {
            public SearchValueValidator(
                string inputStringToBeParsed,
                Action<ISearchValue> validator)
            {
                InputToBeParsed = inputStringToBeParsed;
                Validator = validator;
            }

            public string InputToBeParsed { get; }

            public Action<ISearchValue> Validator { get; set; }
        }

        private class CompositeSearchValueValidator
            : SearchValueValidator
        {
            public CompositeSearchValueValidator(
                Coding expectedCoding,
                string inputStringtoBeParsed,
                Action<ISearchValue> validator)
                : base(inputStringtoBeParsed, validator)
            {
                ExpectedCoding = expectedCoding;
            }

            public Coding ExpectedCoding { get; set; }
        }

        private class TestResource : Resource
        {
            public override IDeepCopyable DeepCopy()
            {
                return null;
            }
        }
    }
}
