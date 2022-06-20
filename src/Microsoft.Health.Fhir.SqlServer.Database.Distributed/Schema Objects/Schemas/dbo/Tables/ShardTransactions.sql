--DROP TABLE dbo.ShardTransactions
GO
CREATE TABLE dbo.ShardTransactions
(
     TransactionId bigint NOT NULL
    ,EndDate       datetime NOT NULL CONSTRAINT DF_ShardTransactions_EndDate DEFAULT getUTCdate()

     CONSTRAINT PKC_ShardTransactions_TransactionId PRIMARY KEY CLUSTERED (TransactionId)
)
GO
