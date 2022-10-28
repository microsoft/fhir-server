--DROP TABLE dbo.Watchdogs
GO
CREATE TABLE dbo.Watchdogs
  (
       Watchdog            varchar(100)  NOT NULL
      ,LeaseHolder         varchar(100)  NOT NULL CONSTRAINT DF_Watchdogs_LeaseHolder DEFAULT ''
      ,LeaseEndDate        datetime      NOT NULL CONSTRAINT DF_Watchdogs_LeaseEndDate DEFAULT 0
      ,RemainingLeaseSec   AS datediff(second,getUTCdate(),LeaseEndDate)
      ,LeaseRequestor      varchar(100)  NOT NULL CONSTRAINT DF_Watchdogs_LeaseRequestor DEFAULT ''
      ,LeaseRequestDate    datetime      NOT NULL CONSTRAINT DF_Watchdogs_LeaseRequestDate DEFAULT 0
      ,LastExecutionDate   datetime      NULL

	      CONSTRAINT PKC_Watchdogs_Watchdog PRIMARY KEY CLUSTERED (Watchdog)
  )
GO
