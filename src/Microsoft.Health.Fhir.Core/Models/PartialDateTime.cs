// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Models
{
    /// <summary>
    /// Represents a partial date time.
    /// </summary>
    public class PartialDateTime
    {
        public static readonly PartialDateTime MinValue = new PartialDateTime(DateTimeOffset.MinValue);
        public static readonly PartialDateTime MaxValue = new PartialDateTime(DateTimeOffset.MaxValue);

        private const string YearCapture = "year";
        private const string MonthCapture = "month";
        private const string DayCapture = "day";
        private const string HourCapture = "hour";
        private const string MinuteCapture = "minute";
        private const string SecondCapture = "second";
        private const string FractionCapture = "fraction";
        private const string TimeZoneCapture = "timeZone";

        // There is a difference between how the date should be parsed for storing and how the date should be parsed for searching.
        // For date that should be stored, the time zone information must be present if time is specified.
        // From spec: http://hl7.org/fhir/STU3/datatypes.html#datetime, "If hours and minutes are specified, a time zone SHALL be populated."
        // However, if the date is being parsed for searching, then the time zone information is optional.
        // From spec: http://hl7.org/fhir/STU3/search.html#date, "the minutes SHALL be present if an hour is present, and you SHOULD provide a time zone if the time part is present."
        // This regular expression allows the time zone to be optional.
        private static readonly Regex DateTimeRegex = new Regex(
            $@"-?(?<{YearCapture}>[0-9]{{4}})(-(?<{MonthCapture}>0[1-9]|1[0-2])(-(?<{DayCapture}>0[0-9]|[1-2][0-9]|3[0-1])(T(?<{HourCapture}>[01][0-9]|2[0-3]):(?<{MinuteCapture}>[0-5][0-9])(:((?<{SecondCapture}>[0-5][0-9])(?<{FractionCapture}>\.[0-9]+)?))?(?<{TimeZoneCapture}>Z|(\+|-)((0[0-9]|1[0-3]):[0-5][0-9]|14:00))?)?)?)?",
            RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.ExplicitCapture);

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
        public PartialDateTime(
            int year,
            int? month = null,
            int? day = null,
            int? hour = null,
            int? minute = null,
            int? second = null,
            decimal? fraction = null,
            TimeSpan? utcOffset = null)
        {
            // Validate the parameters. Partial date is supported but the value must be specified from left to right.
            // E.g., If month is not specified, then day, hour, minute and etc. cannot be specified.
            (string Name, bool HasValue)[] parameters = new[]
            {
                (nameof(month), month.HasValue),
                (nameof(day), day.HasValue),
                (nameof(hour), hour.HasValue),
                (nameof(minute), minute.HasValue),
                (nameof(second), second.HasValue),
                (nameof(fraction), fraction.HasValue),
            };

            bool previousParamHasValue = true;
            string firstParamWithNullValue = null;

            for (int i = 0; i < parameters.Length; i++)
            {
                var currentParameter = parameters[i];

                if (currentParameter.HasValue)
                {
                    if (!previousParamHasValue)
                    {
                        // The current parameter has value but previous one doesn't. This is an invalid state.
                        throw new ArgumentException(
                            $"The {currentParameter.Name} portion of a date cannot be specified if the {firstParamWithNullValue} portion is not specified.",
                            currentParameter.Name);
                    }
                }

                if (!currentParameter.HasValue && firstParamWithNullValue == null)
                {
                    firstParamWithNullValue = currentParameter.Name;
                }

                previousParamHasValue = currentParameter.HasValue;
            }

            if (hour != null)
            {
                if (minute == null)
                {
                    // If hour is specified, then minutes must be specified.
                    throw new ArgumentException(
                        $"The '{nameof(minute)}' portion of a date must be specified if '{nameof(hour)}' is specified.",
                        nameof(minute));
                }

                if (utcOffset == null)
                {
                    // If hour and minute are specified, then the timezone offset must be specified
                    // per spec (http://hl7.org/fhir/datatypes.html#dateTime).
                    // However, in search queries, the time zone information is optional (http://hl7.org/fhir/search.html#date).
                    // The parsing logic will default to UTC time zone if the time zone information is not specified in the search query.
                    throw new ArgumentException(
                        $"The '{nameof(utcOffset)}' portion of a date must be specified if '{nameof(hour)}' and '{nameof(minute)}' are specified.",
                        nameof(utcOffset));
                }
            }

            // Validate the range of each parameter.
            ValidateRange(year, 1, 9999, nameof(year));
            ValidateRange(month, 1, 12, nameof(month));

            if (month.HasValue)
            {
                ValidateRange(day, 1, DateTime.DaysInMonth(year, month.Value), nameof(day));
            }

            ValidateRange(hour, 0, 23, nameof(hour));
            ValidateRange(minute, 0, 59, nameof(minute));
            ValidateRange(second, 0, 59, nameof(second));

            if (fraction != null)
            {
                EnsureArg.IsInRange(fraction.Value, 0, 0.9999999m, nameof(fraction));
            }

            Year = year;
            Month = month;
            Day = day;
            Hour = hour;
            Minute = minute;
            Second = second;
            Fraction = fraction;
            UtcOffset = utcOffset;

            void ValidateRange(int? value, int min, int max, string paramName)
            {
                if (value != null)
                {
                    EnsureArg.IsInRange(value.Value, min, max, paramName);
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PartialDateTime"/> class.
        /// </summary>
        /// <param name="dateTimeOffset">The date time offset to populate the <see cref="PartialDateTime"/> from.</param>
        public PartialDateTime(DateTimeOffset dateTimeOffset)
        {
            var offsetWithFraction = new DateTimeOffset(
                dateTimeOffset.Year,
                dateTimeOffset.Month,
                dateTimeOffset.Day,
                dateTimeOffset.Hour,
                dateTimeOffset.Minute,
                dateTimeOffset.Second,
                dateTimeOffset.Offset);

            decimal fraction = (decimal)dateTimeOffset.Subtract(offsetWithFraction).Ticks / TimeSpan.TicksPerSecond;

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
        public int Year { get; }

        /// <summary>
        /// The optional month component.
        /// </summary>
        public int? Month { get; }

        /// <summary>
        /// The optional day component.
        /// </summary>
        public int? Day { get; }

        /// <summary>
        /// The optional hour component.
        /// </summary>
        public int? Hour { get; }

        /// <summary>
        /// The optional minute component.
        /// </summary>
        public int? Minute { get; }

        /// <summary>
        /// The optional second component.
        /// </summary>
        public int? Second { get; }

        /// <summary>
        /// The optional fraction component representing the fraction of second up to 7 digits.
        /// </summary>
        public decimal? Fraction { get; }

        /// <summary>
        /// The optional UTC offset.
        /// </summary>
        public TimeSpan? UtcOffset { get; }

        /// <summary>
        /// Parses the string value to an instance of <see cref="PartialDateTime"/>.
        /// </summary>
        /// <param name="s">The string to be parsed.</param>
        /// <returns>An instance of <see cref="PartialDateTime"/>.</returns>
        public static PartialDateTime Parse(string s)
        {
            EnsureArg.IsNotNullOrWhiteSpace(s, nameof(s));

            Match match = DateTimeRegex.Match(s);

            if (!match.Success)
            {
                // The input value cannot be parsed correctly.
                throw new FormatException("Input string was not in a correct format.");
            }

            int year = int.Parse(match.Groups[YearCapture].Value);
            int? month = ParseDateTimePart(MonthCapture);
            int? day = ParseDateTimePart(DayCapture);
            int? hour = ParseDateTimePart(HourCapture);
            int? minute = ParseDateTimePart(MinuteCapture);
            int? second = ParseDateTimePart(SecondCapture);

            string fractionInString = match.Groups[FractionCapture]?.Value;

            decimal? fraction = null;

            if (!string.IsNullOrEmpty(fractionInString))
            {
                fraction = decimal.Parse(fractionInString, CultureInfo.InvariantCulture);
            }

            TimeSpan? utcOffset = null;

            string timeZone = match.Groups[TimeZoneCapture]?.Value;

            if (timeZone == "Z")
            {
                utcOffset = TimeSpan.FromMinutes(0);
            }
            else if (!string.IsNullOrEmpty(timeZone))
            {
                utcOffset = DateTimeOffset.ParseExact(timeZone, "zzz", CultureInfo.InvariantCulture).Offset;
            }

            // In search queries, the time zone information is optional (http://hl7.org/fhir/search.html#date).
            // The regular expression will not capture the time zone information unless hour and minutes are present.
            // If hour and minutes are specified but time zone information is not, then we will default to UTC
            // since all dates without time zone is stored in UTC timestamp on the server.
            if (hour != null && utcOffset == null)
            {
                utcOffset = TimeSpan.FromMinutes(0);
            }

            try
            {
                return new PartialDateTime(year, month, day, hour, minute, second, fraction, utcOffset);
            }
            catch (Exception ex) when (ex is ArgumentException)
            {
                // The input value was parsed correctly but one of the value provided were out of range.
                throw new FormatException("Input string was not in a correct format. At least one portion of a date was invalid or out of range.", ex);
            }

            int? ParseDateTimePart(string name)
            {
                return (match.Groups[name]?.Value is var stringValue && string.IsNullOrEmpty(stringValue)) ?
                    (int?)null :
                    int.Parse(stringValue);
            }
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

            DateTimeOffset offset = new DateTimeOffset(
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
            StringBuilder sb = new StringBuilder();

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
