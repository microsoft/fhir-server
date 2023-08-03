CREATE TABLE dbo.Transactions
(
     SurrogateIdRangeFirstValue   bigint NOT NULL
    ,SurrogateIdRangeLastValue    bigint NOT NULL
    ,Definition                   varchar(2000) NULL
    ,IsCompleted                  bit NOT NULL CONSTRAINT DF_Transactions_IsCompleted DEFAULT 0 -- is set at the end of commit process
    ,IsSuccess                    bit NOT NULL CONSTRAINT DF_Transactions_IsSuccess DEFAULT 0 -- is set at the end of commit process on success
    ,IsVisible                    bit NOT NULL CONSTRAINT DF_Transactions_IsVisible DEFAULT 0
    ,IsHistoryMoved               bit NOT NULL CONSTRAINT DF_Transactions_IsHistoryMoved DEFAULT 0
    ,CreateDate                   datetime NOT NULL CONSTRAINT DF_Transactions_CreateDate DEFAULT getUTCdate()
    ,EndDate                      datetime NULL -- is populated only for commit change sets at the end of commit process on success or failure
    ,VisibleDate                  datetime NULL -- indicates when transaction data became visible
    ,HistoryMovedDate             datetime NULL
    ,HeartbeatDate                datetime NOT NULL CONSTRAINT DF_Transactions_HeartbeatDate DEFAULT getUTCdate()
    ,FailureReason                varchar(max) NULL -- is populated at the end of data load on failure
    ,IsControlledByClient         bit NOT NULL CONSTRAINT DF_Transactions_IsControlledByClient DEFAULT 1
    ,InvisibleHistoryRemovedDate  datetime NULL

     CONSTRAINT PKC_Transactions_SurrogateIdRangeFirstValue PRIMARY KEY CLUSTERED (SurrogateIdRangeFirstValue)
)

CREATE INDEX IX_IsVisible ON dbo.Transactions (IsVisible)
