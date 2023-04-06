--DROP TABLE WatchdogLeases
GO
CREATE TABLE dbo.WatchdogLeases
  (
       Watchdog            varchar(100)  NOT NULL
      ,LeaseHolder         varchar(100)  NOT NULL CONSTRAINT DF_WatchdogLeases_LeaseHolder DEFAULT ''
      ,LeaseEndTime        datetime      NOT NULL CONSTRAINT DF_WatchdogLeases_LeaseEndTime DEFAULT 0
      ,RemainingLeaseTimeSec AS datediff(second,getUTCdate(),LeaseEndTime)
      ,LeaseRequestor      varchar(100)  NOT NULL CONSTRAINT DF_WatchdogLeases_LeaseRequestor DEFAULT ''
      ,LeaseRequestTime    datetime      NOT NULL CONSTRAINT DF_WatchdogLeases_LeaseRequestTime DEFAULT 0

	   CONSTRAINT PKC_WatchdogLeases_Watchdog PRIMARY KEY CLUSTERED (Watchdog)
  )
GO
