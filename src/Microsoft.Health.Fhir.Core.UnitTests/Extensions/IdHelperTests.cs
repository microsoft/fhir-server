// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Extensions;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Extensions;

public class IdHelperTests
{
    [Fact]
    public void GivenAnOldDateTime_WhenCreatingAGuid_ThenAValidGuidIsGenerated()
    {
        // Arrange
        DateTime dateTime = new DateTime(1600, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        // Act
        Guid sequentialGuid = dateTime.ToSequentialGuid();

        // Assert
        byte[] bytes = sequentialGuid.ToByteArray();
        Assert.True(IdHelper.CheckValid(bytes));

        DateTime retrievedDateTime = sequentialGuid.SequentialGuidToDateTime();

        // Assert
        Assert.Equal(dateTime, retrievedDateTime);
    }

    [Fact]
    public void GivenADateTime_WhenCreatingAGuid_ThenAValidGuidIsGenerated()
    {
        // Arrange
        DateTime dateTime = DateTime.UtcNow;

        // Act
        Guid sequentialGuid = dateTime.ToSequentialGuid();

        // Assert
        byte[] bytes = sequentialGuid.ToByteArray();
        Assert.True(IdHelper.CheckValid(bytes));
    }

    [Fact]
    public void GivenASequentialGuid_WhenConvertingToDateTime_ThenOriginalDateTimeIsRetrieved()
    {
        // Arrange
        DateTime dateTime = DateTime.UtcNow.TruncateToMillisecond();
        Guid sequentialGuid = dateTime.ToSequentialGuid();

        // Act
        DateTime retrievedDateTime = sequentialGuid.SequentialGuidToDateTime();

        // Assert
        Assert.Equal(dateTime, retrievedDateTime);
    }

    [Fact]
    public void GivenASequentialGuid_WhenConvertingToSurrageId_ThenOriginalDateTimeIsRetrieved()
    {
        // Arrange
        DateTime dateTime = DateTime.UtcNow.TruncateToMillisecond();

        Guid originalGuid = dateTime.ToSequentialGuid();
        long originalSurrogate = dateTime.ToId();

        // to from guid
        var guidToSurrogate = originalGuid.SequentialGuidToSurrogateId();
        var guidAgain = guidToSurrogate.SurrogateIdToSequentialGuid();
        Assert.Equal(originalGuid, guidAgain);

        // to from date
        var surrogateToGuid = originalSurrogate.SurrogateIdToSequentialGuid();
        var surrogateAgain = surrogateToGuid.SequentialGuidToSurrogateId();
        Assert.Equal(originalSurrogate, surrogateAgain);

        // results
        Assert.Equal(dateTime, guidAgain.SequentialGuidToDateTime());
        Assert.Equal(dateTime, surrogateAgain.ToDate());
    }

    [Fact]
    public void GivenASequentialGuid_WhenGeneratingMany_ThenTheyCanBeOrdered()
    {
        DateTime dateTime = DateTime.UtcNow.TruncateToMillisecond();

        var one = dateTime.ToSequentialGuid();
        var two = dateTime.AddMilliseconds(1).ToSequentialGuid();
        var three = dateTime.AddYears(1).ToSequentialGuid();

        var list = new List<Guid>
        {
            two,
            three,
            one,
        };

        list.Sort();

        Assert.Collection(
            list,
            x => Assert.Equal(one, x),
            x => Assert.Equal(two, x),
            x => Assert.Equal(three, x));
    }

    [Fact]
    public void GivenANonSequentialGuid_WhenConvertingToDateTime_ThenThrowsArgumentException()
    {
        // Arrange
        Guid nonSequentialGuid = Guid.NewGuid();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => nonSequentialGuid.SequentialGuidToDateTime());
    }
}
