// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Common.Search
{
    public class SearchValueValidationHelper
    {
        public static void ValidateDateTime(string expectedDateTime, ISearchValue sv)
        {
            ValidateDateTime((expectedDateTime, expectedDateTime), sv);
        }

        public static void ValidateDateTime((string StartDateTime, string EndDateTime) expected, ISearchValue sv)
        {
            DateTimeSearchValue dtsv = Assert.IsType<DateTimeSearchValue>(sv);

            DateTimeOffset expectedStartDateTime = PartialDateTime.Parse(expected.StartDateTime)
                .ToDateTimeOffset(
                1,
                (year, month) => 1,
                0,
                0,
                0,
                0,
                TimeSpan.FromMinutes(0));

            DateTimeOffset expectedEndDateTime = PartialDateTime.Parse(expected.EndDateTime)
                .ToDateTimeOffset(
                12,
                (year, month) => DateTime.DaysInMonth(year, month),
                23,
                59,
                59,
                0.9999999m,
                TimeSpan.FromMinutes(0));

            Assert.Equal(expectedStartDateTime, dtsv.Start);
            Assert.Equal(expectedEndDateTime, dtsv.End);
        }

        public static void ValidateNumber(decimal expected, ISearchValue sv)
        {
            NumberSearchValue nsv = Assert.IsType<NumberSearchValue>(sv);

            Assert.Equal(expected, nsv.Low);
        }

        public static void ValidateQuantity(Quantity expected, ISearchValue sv)
        {
            QuantitySearchValue qsv = Assert.IsType<QuantitySearchValue>(sv);

            Assert.Equal(expected.System, qsv.System);
            Assert.Equal(expected.Code, qsv.Code);
            Assert.Equal(expected.Value, qsv.Low);
        }

        public static void ValidateString(string expected, ISearchValue sv)
        {
            StringSearchValue ssv = Assert.IsType<StringSearchValue>(sv);

            Assert.Equal(expected, ssv.String);
        }

        public static void ValidateToken(Token expected, ISearchValue sv)
        {
            TokenSearchValue tsv = Assert.IsType<TokenSearchValue>(sv);

            Assert.Equal(expected.System, tsv.System);
            Assert.Equal(expected.Code, tsv.Code);
            Assert.Equal(expected.Text, tsv.Text);
        }

        public static void ValidateUri(string expected, ISearchValue sv)
        {
            UriSearchValue usv = Assert.IsType<UriSearchValue>(sv);

            Assert.Equal(expected, usv.Uri);
        }

        public class Token
        {
            public Token(string system = null, string code = null, string text = null)
            {
                System = system;
                Code = code;
                Text = text;
            }

            public string System { get; set; }

            public string Code { get; set; }

            public string Text { get; set; }
        }
    }
}
