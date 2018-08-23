// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Xunit;
using static Microsoft.Health.Fhir.Tests.Common.Search.SearchValueValidationHelper;
using static Microsoft.Health.Fhir.Tests.Integration.Features.Search.TestHelper;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Search.Legacy.Manifests
{
    public abstract class ResourceTypeManifestManagerTests<TResource>
        where TResource : Resource
    {
        private readonly ResourceTypeManifestManager _resourceTypeManifestManager;

        public ResourceTypeManifestManagerTests()
        {
            SearchParamDefinitionManager searchParamDefinitionManager = new SearchParamDefinitionManager();
            SearchParamFactory searchParamFactory = new SearchParamFactory(searchParamDefinitionManager);
            _resourceTypeManifestManager = new ResourceTypeManifestManager(searchParamFactory, searchParamDefinitionManager, new NullLogger<ResourceTypeManifestManager>());
        }

        protected abstract TResource Resource { get; }

        protected ResourceTypeManifest Manifest => _resourceTypeManifestManager.GetManifest(Resource.GetType());

        protected void TestTokenSearchParam(string paramName, Action setup, params IEnumerable<Coding>[] expected)
        {
            List<Coding> allExpected = new List<Coding>();

            foreach (IEnumerable<Coding> expectedValues in expected)
            {
                allExpected.AddRange(expectedValues);
            }

            TestSearchParam(paramName, setup, ValidateTokenWithText, allExpected.ToArray());
        }

        protected void TestTokenSearchParam(string paramName, Action setup, params Coding[] expected)
        {
            TestSearchParam(paramName, setup, ValidateTokenWithText, expected);
        }

        protected void TestSearchParam<TValue>(string paramName, Action setup, Action<TValue, ISearchValue> validator, params TValue[] expected)
        {
            setup();

            IEnumerable<ISearchValue> values = Manifest.GetSearchParam(paramName).ExtractValues(Resource);

            Assert.NotNull(values);
            Assert.Collection(
                values,
                expected.Select(e => new Action<ISearchValue>(sv => validator(e, sv))).ToArray());
        }

        protected void TestActive(Expression<Func<TResource, bool?>> property)
        {
            TestTokenSearchParam(
                "active",
                () =>
                {
                    SetPropertyValue(property, true);
                },
                CodingTrue);
        }

        protected void TestAddressCity(Expression<Func<TResource, List<Address>>> property)
        {
            TestSearchParam(
                "address-city",
                () =>
                {
                    var addresses = new List<Address>()
                    {
                        new Address() { City = String1 },
                        new Address() { City = String2 },
                    };

                    SetPropertyValue(property, addresses);
                },
                ValidateString,
                String1,
                String2);
        }

        protected void TestAddressCountry(Expression<Func<TResource, List<Address>>> property)
        {
            TestSearchParam(
                "address-country",
                () =>
                {
                    var addresses = new List<Address>()
                    {
                        new Address() { Country = String1 },
                        new Address() { Country = String2 },
                    };

                    SetPropertyValue(property, addresses);
                },
                ValidateString,
                String1,
                String2);
        }

        protected void TestAddressPostalCode(Expression<Func<TResource, List<Address>>> property)
        {
            TestSearchParam(
                "address-postalcode",
                () =>
                {
                    var addresses = new List<Address>()
                    {
                        new Address() { PostalCode = String1 },
                        new Address() { PostalCode = String2 },
                    };

                    SetPropertyValue(property, addresses);
                },
                ValidateString,
                String1,
                String2);
        }

        protected void TestAddressState(Expression<Func<TResource, List<Address>>> property)
        {
            TestSearchParam(
                "address-state",
                () =>
                {
                    var addresses = new List<Address>()
                    {
                        new Address() { State = String1 },
                        new Address() { State = String2 },
                    };

                    SetPropertyValue(property, addresses);
                },
                ValidateString,
                String1,
                String2);
        }

        protected void TestAddressUse(Expression<Func<TResource, List<Address>>> property)
        {
            TestTokenSearchParam(
                "address-use",
                () =>
                {
                    var addresses = new List<Address>()
                    {
                        new Address() { Use = Address.AddressUse.Home },
                        new Address() { Use = Address.AddressUse.Work },
                    };

                    SetPropertyValue(property, addresses);
                },
                new Coding("http://hl7.org/fhir/address-use", "home"),
                new Coding("http://hl7.org/fhir/address-use", "work"));
        }

        protected void TestBirthdate(Expression<Func<TResource, string>> property)
        {
            TestSearchParam(
                "birthdate",
                () =>
                {
                    SetPropertyValue(property, DateTime1);
                },
                ValidateDateTime,
                DateTime1);
        }

        protected void TestEmail(Expression<Func<TResource, List<ContactPoint>>> property)
        {
            TestTokenSearchParam(
                "email",
                () =>
                {
                    var contacts = new List<ContactPoint>()
                    {
                        new ContactPoint(ContactPoint.ContactPointSystem.Email, null, String1),
                        new ContactPoint(ContactPoint.ContactPointSystem.Email, ContactPoint.ContactPointUse.Home, String2),
                        new ContactPoint(ContactPoint.ContactPointSystem.Phone, null, String3),
                    };

                    SetPropertyValue(property, contacts);
                },
                new Coding(null, String1),
                new Coding("home", String2));
        }

        protected void TestFamily(Expression<Func<TResource, List<HumanName>>> property)
        {
            TestSearchParam(
                "family",
                () =>
                {
                    var names = new List<HumanName>()
                    {
                        new HumanName() { Family = String1 },
                        new HumanName() { Family = String2 },
                    };

                    SetPropertyValue(property, names);
                },
                ValidateString,
                String1,
                String2);
        }

        protected void TestGender(Expression<Func<TResource, AdministrativeGender?>> property)
        {
            TestTokenSearchParam(
                "gender",
                () =>
                {
                    SetPropertyValue(property, AdministrativeGender.Unknown);
                },
                new Coding("http://hl7.org/fhir/administrative-gender", "unknown"));
        }

        protected void TestGiven(Expression<Func<TResource, List<HumanName>>> property)
        {
            TestSearchParam(
                "given",
                () =>
                {
                    var names = new List<HumanName>()
                    {
                        new HumanName() { Given = new string[] { String1 } },
                        new HumanName() { Given = new string[] { String2, String3 } },
                    };

                    SetPropertyValue(property, names);
                },
                ValidateString,
                String1,
                String2,
                String3);
        }

        protected void TestIdentifier(Expression<Func<TResource, List<Identifier>>> property)
        {
            const string identifierText = "identifier2";

            TestTokenSearchParam(
                "identifier",
                () =>
                {
                    var identifier1 = new Identifier(Coding1WithText.System, Coding1WithText.Code);
                    var identifier2 = new Identifier(Coding2.System, Coding2.Code)
                    {
                        Type = new CodeableConcept()
                        {
                            Text = identifierText,
                        },
                    };

                    var identifiers = new List<Identifier>
                    {
                        identifier1,
                        identifier2,
                    };

                    SetPropertyValue(property, identifiers);
                },
                new Coding(Coding1WithText.System, Coding1WithText.Code),
                new Coding(Coding2.System, Coding2.Code, identifierText));
        }

        protected void TestPhone(Expression<Func<TResource, List<ContactPoint>>> property)
        {
            TestTokenSearchParam(
                "phone",
                () =>
                {
                    var contacts = new List<ContactPoint>()
                    {
                        new ContactPoint(ContactPoint.ContactPointSystem.Phone, null, String1),
                        new ContactPoint(ContactPoint.ContactPointSystem.Phone, ContactPoint.ContactPointUse.Home, String2),
                        new ContactPoint(ContactPoint.ContactPointSystem.Email, null, String3),
                    };

                    SetPropertyValue(property, contacts);
                },
                new Coding(null, String1),
                new Coding("home", String2));
        }

        protected void TestTelecom(Expression<Func<TResource, List<ContactPoint>>> property)
        {
            TestTokenSearchParam(
                "telecom",
                () =>
                {
                    var contacts = new List<ContactPoint>()
                    {
                        new ContactPoint(ContactPoint.ContactPointSystem.Email, null, String1),
                        new ContactPoint(ContactPoint.ContactPointSystem.Phone, ContactPoint.ContactPointUse.Home, String2),
                        new ContactPoint(ContactPoint.ContactPointSystem.Pager, ContactPoint.ContactPointUse.Mobile, String3),
                    };

                    SetPropertyValue(property, contacts);
                },
                new Coding(null, String1),
                new Coding("home", String2),
                new Coding("mobile", String3));
        }

        protected void TestName(Expression<Func<TResource, List<HumanName>>> property)
        {
            TestSearchParam(
                "name",
                () =>
                {
                    var names = new List<HumanName>()
                    {
                        new HumanName() { Given = new[] { String1 } },
                        new HumanName() { Family = String2 },
                        new HumanName() { Text = String3 },
                    };

                    SetPropertyValue(property, names);
                },
                ValidateString,
                String1,
                String2,
                String3);
        }

        protected void TestAddress(Expression<Func<TResource, List<Address>>> property)
        {
            TestSearchParam(
                "address",
                () =>
                {
                    var addresses = new List<Address>()
                    {
                        new Address() { City = String1 },
                        new Address() { State = String2 },
                        new Address() { Country = String3 },
                    };

                    SetPropertyValue(property, addresses);
                },
                ValidateString,
                String1,
                String2,
                String3);
        }

        protected void TestVersion(Expression<Func<TResource, string>> property)
        {
            TestTokenSearchParam(
                "version",
                () => { SetPropertyValue(property, String1); },
                new Coding(null, String1));
        }

        private void SetPropertyValue<TProperty>(Expression<Func<TResource, TProperty>> property, TProperty value)
        {
            var memberExpression = (MemberExpression)property.Body;
            var paramExpression = Expression.Parameter(typeof(TProperty), "value");
            var setterExpression = Expression.Lambda<Action<TResource, TProperty>>(
                Expression.Assign(memberExpression, paramExpression), property.Parameters[0], paramExpression);

            setterExpression.Compile()(Resource, value);
        }
    }
}
