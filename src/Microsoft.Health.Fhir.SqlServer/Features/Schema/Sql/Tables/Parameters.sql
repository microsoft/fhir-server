-- Generic table to store application specific parameters
-- DROP TABLE Parameters
GO
CREATE TABLE dbo.Parameters 
  (
     Id          varchar(100)     NOT NULL
    ,Date        datetime         NULL
    ,Number      float            NULL
    ,Bigint      bigint           NULL
    ,Char        varchar(4000)    NULL
    ,Binary      varbinary(max)   NULL
    
    ,UpdatedDate datetime         NULL
    ,UpdatedBy   nvarchar(255)    NULL
    
     CONSTRAINT PKC_Parameters_Id PRIMARY KEY CLUSTERED (Id) WITH (IGNORE_DUP_KEY = ON)
  )
GO
CREATE TABLE dbo.ParametersHistory 
  (
     ChangeId    int              NOT NULL IDENTITY(1,1)
    ,Id          varchar(100)     NOT NULL
    ,Date        datetime         NULL
    ,Number      float            NULL
    ,Bigint      bigint           NULL
    ,Char        varchar(4000)    NULL
    ,Binary      varbinary(max)   NULL
    ,UpdatedDate datetime         NULL
    ,UpdatedBy   nvarchar(255)    NULL
  )
GO
--CREATE TRIGGER dbo.ParametersInsUpdDel ON dbo.Parameters
--FOR INSERT, UPDATE, DELETE
--AS
--BEGIN
--  INSERT INTO dbo.ParametersHistory
--      (
--           Id
--          ,Date
--          ,Number
--          ,Bigint
--          ,Char
--          ,Binary
--          ,UpdatedDate
--          ,UpdatedBy
--      )
--    SELECT Id
--          ,Date
--          ,Number
--          ,Bigint
--          ,Char
--          ,Binary
--          ,UpdatedDate
--          ,UpdatedBy
--      FROM Deleted

--  UPDATE A
--    SET UpdatedDate = getUTCdate() 
--       ,UpdatedBy = left(system_user, 100)
--    FROM dbo.Parameters A 
--         JOIN Inserted B 
--           ON B.Id = A.Id
--  RETURN
--END
GO
