/*************************************************************
    This migration removes primary key from few tables
**************************************************************/

EXEC dbo.LogSchemaMigrationProgress 'Beginning schema migration to version 28.';
GO

/*************************************************************
        DateTimeSearchParam table
**************************************************************/
--Scenarios
--1. Start is a valid date 2013-04-02 08:30:10.0000000 and end is a high date 9999-12-31 23:59:59.9999999 => 1
--2. Start is an initial date 0001-01-01 00:00:00.0000000 and end is a valid date 2013-04-02 08:30:10.9999999 => 1
--3. Start is a valid date 2013-04-02 08:30:10.0000000 and end is a valid date 2015-04-02 08:30:10.0000000 where end > start => 1
--4. Start is a valid date 2015-04-02 08:30:10.0000000 and end is a valid date 2013-04-02 08:30:10.0000000 where end < start => 1
--5. Start is a valid date 2015-04-02 08:30:10.0000000 and end is a valid date 2015-04-02 10:30:10.0000000 where end = start => 0

-- Backfill table dbo.DateTimeSearchParam with correct IsLongerThanADay value
EXEC dbo.LogSchemaMigrationProgress 'Back-fill column IsLongerThanADay into the table dbo.DateTimeSearchParam';

Update DateTimeSearchParam
set IsLongerThanADay = 1
where Cast(CONVERT(varchar,StartDateTime,20) as Date) != Cast(CONVERT(varchar,EndDateTime,20) as Date)

/*************************************************************
        TokenDateTimeCompositeSearchParam table
**************************************************************/
--Scenarios
--1. Start is a valid date 2013-04-02 08:30:10.0000000 and end is a high date 9999-12-31 23:59:59.9999999 => 1
--2. Start is an initial date 0001-01-01 00:00:00.0000000 and end is a valid date 2013-04-02 08:30:10.9999999 => 1
--3. Start is a valid date 2013-04-02 08:30:10.0000000 and end is a valid date 2015-04-02 08:30:10.0000000 where end > start => 1
--4. Start is a valid date 2015-04-02 08:30:10.0000000 and end is a valid date 2013-04-02 08:30:10.0000000 where end < start => 1
--5. Start is a valid date 2015-04-02 08:30:10.0000000 and end is a valid date 2015-04-02 10:30:10.0000000 where end = start => 0

-- Backfill table dbo.TokenDateTimeCompositeSearchParam with correct IsLongerThanADay value
EXEC dbo.LogSchemaMigrationProgress 'Back-fill column IsLongerThanADay2 into the table dbo.TokenDateTimeCompositeSearchParam';

Update TokenDateTimeCompositeSearchParam
set IsLongerThanADay2 = 1
where Cast(CONVERT(varchar,StartDateTime2,20) as Date) != Cast(CONVERT(varchar,EndDateTime2,20) as Date)
