// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.SearchValues
{
    /// <summary>
    /// Represents a date time search value.
    /// </summary>
    /// <remarks>
    /// The date time value is expressed in range. An instance of time can be
    /// thought as a range with the start and end time being the same. The start
    /// and end datetimeoffsets are always in UTC. </remarks>
    [SuppressMessage("ReSharper", "CA1036", Justification = "Used for search value comparison.")]
    public class DateTimeSearchValue : ISearchValue, ISupportSortSearchValue, IRangedComparable
    {
        /// <summary>
        /// Original datetime value, if it not a range.
        /// </summary>
        private readonly PartialDateTime _originalDate;

        /// <summary>
        /// Initializes a new instance of the <see cref="DateTimeSearchValue"/> class.
        /// </summary>
        /// <param name="dateTime">The date time.</param>
        public DateTimeSearchValue(PartialDateTime dateTime)
            : this(dateTime, dateTime)
        {
            _originalDate = dateTime;

            // The date can be expressed with start and end.
            // If an instance of time is expressed (e.g., date + time), then the start and end
            // will be at the same instance. If a partial date is specified (e.g., 2010-01), then
            // missing parts will be filled in and is equivalent to 2010-01-01T00:00:00.0000000Z as start
            // and 2010-01-31T23:59:59.9999999Z as end.
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DateTimeSearchValue"/> class.
        /// </summary>
        /// <param name="dateTimeOffset">The date time offset.</param>
        public DateTimeSearchValue(DateTimeOffset dateTimeOffset)
            : this(new PartialDateTime(dateTimeOffset))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DateTimeSearchValue"/> class.
        /// </summary>
        /// <param name="startDateTime">The start date time.</param>
        /// <param name="endDateTime">The end date time.</param>
        public DateTimeSearchValue(PartialDateTime startDateTime, PartialDateTime endDateTime)
        {
            Start = startDateTime.ToDateTimeOffset().ToUniversalTime();

            End = endDateTime.ToDateTimeOffset(
                defaultMonth: 12,
                defaultDaySelector: (year, month) => DateTime.DaysInMonth(year, month),
                defaultHour: 23,
                defaultMinute: 59,
                defaultSecond: 59,
                defaultFraction: 0.9999999m,
                defaultUtcOffset: TimeSpan.Zero).ToUniversalTime();
        }

        /// <summary>
        /// Gets the start date time.
        /// </summary>
        public DateTimeOffset Start { get; }

        /// <summary>
        /// Gets the end date time.
        /// </summary>
        public DateTimeOffset End { get; }

        /// <inheritdoc />
        public bool IsValidAsCompositeComponent => true;

        public bool IsMin { get; set; }

        public bool IsMax { get; set; }

        /// <summary>
        /// Parses the string value to an instance of <see cref="DateTimeSearchValue"/>.
        /// </summary>
        /// <param name="s">The string to be parsed.</param>
        /// <returns>An instance of <see cref="DateTimeSearchValue"/>.</returns>
        public static DateTimeSearchValue Parse(string s)
        {
            EnsureArg.IsNotNullOrWhiteSpace(s, nameof(s));

            PartialDateTime dateTime = PartialDateTime.Parse(s);

            return new DateTimeSearchValue(dateTime);
        }

        /// <summary>
        /// Parses the string value to an instance of <see cref="DateTimeSearchValue"/>.
        /// </summary>
        /// <param name="startDateTime">The string to be parsed as start time.</param>
        /// <param name="endDateTime">The string to be parsed as end time.</param>
        /// <returns>An instance of <see cref="DateTimeSearchValue"/>.</returns>
        public static DateTimeSearchValue Parse(string startDateTime, string endDateTime)
        {
            EnsureArg.IsNotNullOrWhiteSpace(startDateTime, nameof(startDateTime));
            EnsureArg.IsNotNullOrWhiteSpace(endDateTime, nameof(endDateTime));

            PartialDateTime startPartial = PartialDateTime.Parse(startDateTime);
            PartialDateTime endPartial = PartialDateTime.Parse(endDateTime);

            return new DateTimeSearchValue(startPartial, endPartial);
        }

        /// <inheritdoc />
        public void AcceptVisitor(ISearchValueVisitor visitor)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            visitor.Visit(this);
        }

        public bool Equals([AllowNull] ISearchValue other)
        {
            if (other == null)
            {
                return false;
            }

            var dateTimeSearchValueOther = other as DateTimeSearchValue;

            if (dateTimeSearchValueOther == null)
            {
                return false;
            }

            return Start == dateTimeSearchValueOther.Start && End == dateTimeSearchValueOther.End;
        }

        public int CompareTo(ISupportSortSearchValue other, ComparisonRange range)
        {
            if (other == null)
            {
                throw new ArgumentException("Value to be compared to cannot be null");
            }

            var otherValue = other as DateTimeSearchValue;
            if (otherValue == null)
            {
                throw new ArgumentException($"Value to be compared should be of type {typeof(DateTimeSearchValue)}");
            }

            switch (range)
            {
                case ComparisonRange.Min:
                    return DateTimeOffset.Compare(Start, otherValue.Start);
                case ComparisonRange.Max:
                    return DateTimeOffset.Compare(End, otherValue.End);
                default:
                    throw new ArgumentOutOfRangeException(nameof(range));
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            if (_originalDate != null)
            {
                return _originalDate.ToString();
            }

            return $"{Start.ToString("o")}-{End.ToString("o")}";
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCode.Combine(
                Start.GetHashCode(), End.GetHashCode());
        }
    }
}
