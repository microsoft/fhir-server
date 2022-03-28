/*************************************************************
    This migration removes primary key from few tables
**************************************************************/

EXEC dbo.LogSchemaMigrationProgress 'Beginning schema migration to version 28.';
GO

/*************************************************************
        DateTimeSearchParam table
**************************************************************/
/*
Scenarios (We need to add year diff because datediff function results in an overflow due to high end date values)
    1. StartDate is a valid date 2013-04-02 08:30:10.0000000 and EndDate is a high date 9999-12-31 23:59:59.9999999 => 1
    2. StartDate is an initial date 0001-01-01 00:00:00.0000000 and EndDate is a valid date 2013-04-02 08:30:10.9999999 => 1
    3. StartDate is a valid date 2013-04-02 08:30:10.0000000 and EndDate is a valid date 2015-04-02 08:30:10.0000000 where end > start => 1
    4. StartDate is a valid date 2015-04-02 08:30:10.0000000 and EndDate is a valid date 2013-04-02 08:30:10.0000000 where end < start => 1
    5. StartDate is a valid date 2015-04-02 08:30:10.0000000 and EndDate is a valid date 2015-04-02 10:30:10.0000000 where end = start => 0
    6. StartDate is 9998-12-31 23:59:59.9999999 and EndDate is 9999-01-01 01:59:59.9999999 => Though the year diff is 1 they are not apart by 1 day, hence comparing the year diff as > 1
*/

-- Backfill table dbo.DateTimeSearchParam with correct IsLongerThanADay value
EXEC dbo.LogSchemaMigrationProgress 'Back-fill column IsLongerThanADay into the table dbo.DateTimeSearchParam';

Update DateTimeSearchParam
set IsLongerThanADay = case when ABS(DATEPART(year, StartDateTime) - DATEPART(year, EndDateTime)) > 1  then 1
							when ABS(DATEDIFF(SECOND, StartDateTime, EndDateTime)) > 86400 then 1
							else 0
end

/*************************************************************
        TokenDateTimeCompositeSearchParam table
**************************************************************/
/*
Scenarios (We need to add year diff because datediff function results in an overflow due to high end date values)
    1. StartDate is a valid date 2013-04-02 08:30:10.0000000 and EndDate is a high date 9999-12-31 23:59:59.9999999 => 1
    2. StartDate is an initial date 0001-01-01 00:00:00.0000000 and EndDate is a valid date 2013-04-02 08:30:10.9999999 => 1
    3. StartDate is a valid date 2013-04-02 08:30:10.0000000 and EndDate is a valid date 2015-04-02 08:30:10.0000000 where end > start => 1
    4. StartDate is a valid date 2015-04-02 08:30:10.0000000 and EndDate is a valid date 2013-04-02 08:30:10.0000000 where end < start => 1
    5. StartDate is a valid date 2015-04-02 08:30:10.0000000 and EndDate is a valid date 2015-04-02 10:30:10.0000000 where end = start => 0
    6. StartDate is 9998-12-31 23:59:59.9999999 and EndDate is 9999-01-01 01:59:59.9999999 => Though the year diff is 1 they are not apart by 1 day, hence comparing the year diff as > 1
*/

-- Backfill table dbo.TokenDateTimeCompositeSearchParam with correct IsLongerThanADay value
EXEC dbo.LogSchemaMigrationProgress 'Back-fill column IsLongerThanADay2 into the table dbo.TokenDateTimeCompositeSearchParam';

Update TokenDateTimeCompositeSearchParam
set IsLongerThanADay2 = case when ABS(DATEPART(year, StartDateTime2) - DATEPART(year, EndDateTime2)) > 1  then 1
							when ABS(DATEDIFF(SECOND, StartDateTime2, EndDateTime2)) > 86400 then 1
							else 0
end
