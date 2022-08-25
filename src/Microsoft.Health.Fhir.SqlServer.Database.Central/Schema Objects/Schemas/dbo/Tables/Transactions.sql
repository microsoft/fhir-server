CREATE PARTITION FUNCTION TransactionsPartitionFunction (tinyint) AS RANGE RIGHT FOR VALUES (0,1,2,3,4,5,6,7)
GO
CREATE PARTITION SCHEME TransactionsPartitionScheme AS PARTITION TransactionsPartitionFunction ALL TO ([PRIMARY])
GO
--DROP TABLE dbo.Transactions
GO
CREATE TABLE dbo.Transactions
(
     PartitionId                  AS isnull(convert(tinyint, TransactionId % 8),0) PERSISTED
    ,TransactionId                bigint NOT NULL
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

     CONSTRAINT PKC_Transactions_PartitionId_TransactionId PRIMARY KEY CLUSTERED (PartitionId, TransactionId) ON TransactionsPartitionScheme(PartitionId)
)
GO
CREATE INDEX IX_IsVisible ON dbo.Transactions (IsVisible) ON TransactionsPartitionScheme(PartitionId)
GO
--CREATE INDEX IX_IsHistoryMoved ON dbo.Transactions (IsHistoryMoved) -- TODO: Check perf
GO
CREATE SEQUENCE dbo.TransactionIdSequence AS bigint START WITH 1 INCREMENT BY 1
GO
