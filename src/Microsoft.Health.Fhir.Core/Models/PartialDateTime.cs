// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EnsureThat;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Models
{
    /// <summary>
    /// Represents a partial date time.
    /// </summary>
    public class PartialDateTime
    {
        private static readonly string[] _formats = GenerateDateTimeOffsetFormats();

        private const string YearCapture = "year";
        private const string MonthCapture = "month";
        private const string DayCapture = "day";
        private const string HourCapture = "hour";
        private const string MinuteCapture = "minute";
        private const string SecondCapture = "second";
        private const string FractionCapture = "fraction";
        private const string TimeZoneCapture = "timeZone";
        private const string InvalidTimeZoneCapture = "invalidTimeZone";

        // This regular expression is used to capture which date time parts are specified by the user and which parts are not.
        // This is required because date time parts left blank are used to indicate a time period.
        // For example, 2000 is equivalent to an interval of [2000-01-01T00:00, 2000-12-31T23:59].
        private static readonly Regex DateTimeRegex = new Regex(
            $@"(?<{YearCapture}>\d{{4}})(-(?<{MonthCapture}>\d{{2}}))?(-(?<{DayCapture}>\d{{2}}))?(T(?<{HourCapture}>\d{{2}}))?(:(?<{MinuteCapture}>\d{{2}}))?(:(?<{SecondCapture}>\d{{2}}))?((?<{FractionCapture}>\.\d+))?((?<{TimeZoneCapture}>Z|(\+|-)((\d{{2}}):\d{{2}}))|(?<{InvalidTimeZoneCapture}>Z|(\+|-)((\d):\d{{2}})))?",
            RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.ExplicitCapture);

        public static readonly PartialDateTime MinValue = new PartialDateTime(DateTimeOffset.MinValue);
        public static readonly PartialDateTime MaxValue = new PartialDateTime(DateTimeOffset.MaxValue);

        /// <summary>
        /// Initializes a new instance of the <see cref="PartialDateTime"/> class.
        /// </summary>
        /// <param name="year">The year component.</param>
        /// <param name="month">The optional month component.</param>
        /// <param name="day">The optional day component.</param>
        /// <param name="hour">The optional hour component.</param>
        /// <param name="minute">The optional minute component.</param>
        /// <param name="second">The optional second component.</param>
        /// <param name="fraction">The optional fraction component representing the fraction of second up to 7 digits.</param>
        /// <param name="utcOffset">The optional UTC offset component.</param>
        private PartialDateTime(
            int year,
            int? month = null,
            int? day = null,
            int? hour = null,
            int? minute = null,
            int? second = null,
            decimal? fraction = null,
            TimeSpan? utcOffset = null)
        {
            Year = year;
            Month = month;
            Day = day;
            Hour = hour;
            Minute = minute;
            Second = second;
            Fraction = fraction;
            UtcOffset = utcOffset;
        }

        [JsonConstructor]
        protected PartialDateTime()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PartialDateTime"/> class.
        /// </summary>
        /// <param name="dateTimeOffset">The date time offset to populate the <see cref="PartialDateTime"/> from.</param>
        public PartialDateTime(DateTimeOffset dateTimeOffset)
        {
            decimal fraction = GetFractionFromDateTimeOffset(dateTimeOffset);

            Year = dateTimeOffset.Year;
            Month = dateTimeOffset.Month;
            Day = dateTimeOffset.Day;
            Hour = dateTimeOffset.Hour;
            Minute = dateTimeOffset.Minute;
            Second = dateTimeOffset.Second;
            Fraction = fraction;
            UtcOffset = dateTimeOffset.Offset;
        }

        /// <summary>
        /// The delegate used to select the day from the given <paramref name="year"/> and <paramref name="month"/>.
        /// </summary>
        /// <param name="year">The year</param>
        /// <param name="month">The month.</param>
        /// <returns>The day.</returns>
        public delegate int DaySelector(int year, int month);

        /// <summary>
        /// The year component.
        /// </summary>
        [JsonProperty("year")]
        public int Year { get; private set; }

        /// <summary>
        /// The optional month component.
        /// </summary>
        [JsonProperty("month")]
        public int? Month { get; private set; }

        /// <summary>
        /// The optional day component.
        /// </summary>
        [JsonProperty("day")]
        public int? Day { get; private set; }

        /// <summary>
        /// The optional hour component.
        /// </summary>
        [JsonProperty("hour")]
        public int? Hour { get; private set; }

        /// <summary>
        /// The optional minute component.
        /// </summary>
        [JsonProperty("minute")]
        public int? Minute { get; private set; }

        /// <summary>
        /// The optional second component.
        /// </summary>
        [JsonProperty("second")]
        public int? Second { get; private set; }

        /// <summary>
        /// The optional fraction component representing the fraction of second up to 7 digits.
        /// </summary>
        [JsonProperty("fraction")]
        public decimal? Fraction { get; private set; }

        /// <summary>
        /// The optional UTC offset.
        /// </summary>
        [JsonProperty("utcOffset")]
        public TimeSpan? UtcOffset { get; private set; }

        /// <summary>
        /// Parses the string value to an instance of <see cref="PartialDateTime"/>.
        /// </summary>
        /// <param name="inputString">The string to be parsed.</param>
        /// <returns>An instance of <see cref="PartialDateTime"/>.</returns>
        public static PartialDateTime Parse(string inputString)
        {
            EnsureArg.IsNotNullOrWhiteSpace(inputString, nameof(inputString));

            IFormatProvider provider = CultureInfo.InvariantCulture.DateTimeFormat;

            if (!DateTimeOffset.TryParseExact(inputString, _formats, provider, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsedDateTimeOffset))
            {
                // The input value cannot be parsed correctly.
                throw new FormatException(string.Format(Resources.DateTimeStringIsIncorrectlyFormatted, inputString));
            }

            Match match = DateTimeRegex.Match(inputString);

            int year = int.Parse(match.Groups[YearCapture].Value);
            int? month = GetIsDateTimePartSpecified(MonthCapture) ? parsedDateTimeOffset.Month : null;
            int? day = GetIsDateTimePartSpecified(DayCapture) ? parsedDateTimeOffset.Day : null;
            int? hour = GetIsDateTimePartSpecified(HourCapture) ? parsedDateTimeOffset.Hour : null;
            int? minute = GetIsDateTimePartSpecified(MinuteCapture) ? parsedDateTimeOffset.Minute : null;
            int? second = GetIsDateTimePartSpecified(SecondCapture) ? parsedDateTimeOffset.Second : null;

            decimal? fraction = null;

            if (GetIsDateTimePartSpecified(FractionCapture))
            {
                fraction = GetFractionFromDateTimeOffset(parsedDateTimeOffset);
            }

            // The DateTimeOffset parser allows the time zone hour to be formatted with only one digit.
            // The spec specifies that the time zone hour must have two digits: yyyy-mm-ddThh:mm:ss[Z|(+|-)hh:mm]
            // Use the RegEx to determine if the time zone is correctly formatted or not.
            if (GetIsDateTimePartSpecified(InvalidTimeZoneCapture))
            {
                throw new FormatException(string.Format(Resources.DateTimeStringIsIncorrectlyFormatted, inputString));
            }

            TimeSpan? utcOffset = GetIsDateTimePartSpecified(TimeZoneCapture) ? parsedDateTimeOffset.Offset : null;

            // If hour and minutes are specified but time zone information is not, then we will default to UTC
            // because all dates without time zone information are stored with a UTC timestamp on the server.
            if (hour != null && utcOffset == null)
            {
                utcOffset = TimeSpan.FromMinutes(0);
            }

            return new PartialDateTime(year, month, day, hour, minute, second, fraction, utcOffset);

            bool GetIsDateTimePartSpecified(string name)
            {
                var stringValue = match.Groups[name]?.Value;
                return !string.IsNullOrEmpty(stringValue);
            }
        }

        /// <summary>
        /// Creates the formats array, which outlines how to parse the input string into a DateTimeOffset object.
        /// </summary>
        /// <returns>An array of acceptable DateTimeOffset formats.</returns>
        /// <remarks>
        /// The parser serves two different purposes: storing and searching.
        /// There is a difference between how the date should be parsed for storing and how the date should be parsed for searching.
        /// For date that should be stored, the time zone information must be present if time is specified.
        /// From spec: http://hl7.org/fhir/datatypes.html#datetime, "If hours and minutes are specified, a time zone SHALL be populated."
        /// However, if the date is being parsed for searching, then the time zone information is optional.
        /// From spec: http://hl7.org/fhir/search.html#date, "you SHOULD provide a time zone if the time part is present."
        /// As a result, the formats array allows the time zone to be optional.
        /// </remarks>
        private static string[] GenerateDateTimeOffsetFormats()
        {
            var formats = new List<string> { "yyyy", "yyyy-MM", "yyyy-MM-dd" };

            // From spec: "the minutes SHALL be present if an hour is present".
            var timeFormats = new List<string>
            {
                "yyyy-MM-ddTHH:mm",
                "yyyy-MM-ddTHH:mm:ss",
                "yyyy-MM-ddTHH:mm:ss.f",
                "yyyy-MM-ddTHH:mm:ss.ff",
                "yyyy-MM-ddTHH:mm:ss.fff",
                "yyyy-MM-ddTHH:mm:ss.ffff",
                "yyyy-MM-ddTHH:mm:ss.fffff",
                "yyyy-MM-ddTHH:mm:ss.ffffff",
                "yyyy-MM-ddTHH:mm:ss.fffffff",
            };

            formats.AddRange(timeFormats);

            var timeZoneFormats = new List<string> { "Z", "zzz" };

            // If the time is specified, the time zone could be specified.
            foreach (var timeFormat in timeFormats)
            {
                foreach (var timeZoneFormat in timeZoneFormats)
                {
                    formats.Add(timeFormat + timeZoneFormat);
                }
            }

            return formats.ToArray();
        }

        private static decimal GetFractionFromDateTimeOffset(DateTimeOffset parsedDateTimeOffset)
        {
            var offsetWithoutFraction = new DateTimeOffset(
                parsedDateTimeOffset.Year,
                parsedDateTimeOffset.Month,
                parsedDateTimeOffset.Day,
                parsedDateTimeOffset.Hour,
                parsedDateTimeOffset.Minute,
                parsedDateTimeOffset.Second,
                parsedDateTimeOffset.Offset);

            return (decimal)parsedDateTimeOffset.Subtract(offsetWithoutFraction).Ticks / TimeSpan.TicksPerSecond;
        }

        /// <summary>
        /// Converts the value of the current <see cref="PartialDateTime"/> object to a <see cref="DateTimeOffset"/> value.
        /// </summary>
        /// <param name="defaultMonth">The value to use if month component missing.</param>
        /// <param name="defaultDaySelector">A selector to get the value to use if day component is missing.</param>
        /// <param name="defaultHour">The value to use if hour component is missing.</param>
        /// <param name="defaultMinute">The value to use if minute component is missing.</param>
        /// <param name="defaultSecond">The value to use if second component is missing.</param>
        /// <param name="defaultFraction">The value to use if fraction component is missing.</param>
        /// <param name="defaultUtcOffset">The value to use if UTC offset component is missing.</param>
        /// <returns>An object that represents the date and time of the current <see cref="PartialDateTime"/> object.</returns>
        public DateTimeOffset ToDateTimeOffset(
            int defaultMonth,
            DaySelector defaultDaySelector,
            int defaultHour,
            int defaultMinute,
            int defaultSecond,
            decimal defaultFraction,
            TimeSpan defaultUtcOffset)
        {
            int month = Month ?? defaultMonth;

            var offset = new DateTimeOffset(
                Year,
                month,
                Day ?? defaultDaySelector(Year, month),
                Hour ?? defaultHour,
                Minute ?? defaultMinute,
                Second ?? defaultSecond,
                UtcOffset ?? defaultUtcOffset);

            // Add second, millisecond, and fraction.
            decimal fraction = Fraction ?? defaultFraction;

            offset = offset.AddTicks((long)(fraction * TimeSpan.TicksPerSecond));

            return offset;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var sb = new StringBuilder();

            sb.Append(Year);

            AppendIfNotNull('-', Month);
            AppendIfNotNull('-', Day);

            if (Hour != null)
            {
                sb.AppendFormat("T{0:D2}:{1:D2}", Hour, Minute);

                if (Second != null)
                {
                    if (Fraction == null)
                    {
                        sb.AppendFormat(":{0:D2}", Second);
                    }
                    else
                    {
                        sb.AppendFormat(":{0:00.0000000}", Second + Fraction);
                    }
                }

                sb.AppendFormat("{0:+00;-00}:{1:D2}", UtcOffset.Value.Hours, UtcOffset.Value.Minutes);
            }

            return sb.ToString();

            void AppendIfNotNull(char separator, int? value)
            {
                if (value != null)
                {
                    sb.AppendFormat("{0}{1:D2}", separator, value.Value);
                }
            }
        }
    }
}
