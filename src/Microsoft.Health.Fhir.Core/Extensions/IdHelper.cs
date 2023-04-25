// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Security.Cryptography;
using EnsureThat;
using Microsoft.Health.Core.Extensions;

namespace Microsoft.Health.Fhir.Core.Extensions;

public static class IdHelper
{
    private const int ShiftFactor = 3;

    internal static readonly DateTime MaxDateTime = new DateTime(long.MaxValue >> ShiftFactor, DateTimeKind.Utc).TruncateToMillisecond().AddTicks(-1);
    private static int _guidUniquifierStartIndex = 12;

    public static long ToId(this DateTime dateTime)
    {
        EnsureArg.IsLte(dateTime, MaxDateTime, nameof(dateTime));
        long id = dateTime.TruncateToMillisecond().Ticks << ShiftFactor;

        Debug.Assert(id >= 0, "The ID should not have become negative");
        return id;
    }

    public static DateTime ToDate(this long resourceSurrogateId)
    {
        var dateTime = new DateTime(resourceSurrogateId >> ShiftFactor, DateTimeKind.Utc);
        return dateTime.TruncateToMillisecond();
    }

    public static Guid ToSequentialGuid(this DateTime dateTime, int? uniquifier = null)
    {
        if (dateTime.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Must be a Utc dateTime.");
        }

        var tempGuid = Guid.NewGuid();
        var bytes = tempGuid.ToByteArray();

        // See: https://stackoverflow.com/questions/211498/is-there-a-net-equivalent-to-sql-servers-newsequentialid
        // Used to prevent major changes to the beginning of the sequence
        DateTime time = dateTime.TruncateToMillisecond();
        bytes[3] = (byte)(time.Year / 256);
        bytes[2] = (byte)(time.Year % 256);
        bytes[1] = (byte)time.Month;
        bytes[0] = (byte)time.Day;
        bytes[5] = (byte)time.Hour;
        bytes[4] = (byte)time.Minute;
        bytes[7] = (byte)time.Second;
        bytes[6] = (byte)(time.Millisecond / 256);
        bytes[8] = (byte)(time.Millisecond % 256);

        // set the 10th byte to '1100'
        bytes[9] = 0xc0;

        bytes[10] = 0;
        bytes[11] = 0;

        BitConverter.GetBytes(uniquifier ?? RandomNumberGenerator.GetInt32(0, 9999)).CopyTo(bytes, _guidUniquifierStartIndex);

        return new Guid(bytes);
    }

    public static DateTime SequentialGuidToDateTime(this Guid sequentialGuid)
    {
        byte[] bytes = sequentialGuid.ToByteArray();

        if (!CheckValid(bytes))
        {
            throw new ArgumentException("Not a valid sequential guid.");
        }

        int year = (bytes[3] * 256) + bytes[2];
        int month = bytes[1];
        int day = bytes[0];
        int hour = bytes[5];
        int minute = bytes[4];
        int second = bytes[7];
        int millisecond = (bytes[6] * 256) + bytes[8];

        return new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Utc);
    }

    internal static bool CheckValid(byte[] sequentialGuid)
    {
        // Check if the first part of the 10th byte is '1100'
        return sequentialGuid?.Length == 16 && (sequentialGuid[9] & 0xC0) == 0xC0;
    }

    public static Guid SurrogateIdToSequentialGuid(this long surrogateId)
    {
        int uniquifier = (int)(surrogateId % 10000);

        DateTime dateTime = ToDate(surrogateId);
        Guid sequentialGuid = dateTime.ToSequentialGuid(uniquifier);

        byte[] guidBytes = sequentialGuid.ToByteArray();
        BitConverter.GetBytes(uniquifier).CopyTo(guidBytes, _guidUniquifierStartIndex);

        return new Guid(guidBytes);
    }

    public static long SequentialGuidToSurrogateId(this Guid sequentialGuid)
    {
        DateTime dateTime = SequentialGuidToDateTime(sequentialGuid);
        long ticks = ToId(dateTime);

        byte[] guidBytes = sequentialGuid.ToByteArray();
        int uniquifier = BitConverter.ToInt32(guidBytes, _guidUniquifierStartIndex);

        long surrogateId = ticks + uniquifier;
        return surrogateId;
    }
}
