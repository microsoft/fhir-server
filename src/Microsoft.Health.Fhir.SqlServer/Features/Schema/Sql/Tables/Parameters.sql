
CREATE TABLE dbo.Parameters 
  (
     Id          varchar(100)     NOT NULL
    ,Date        datetime2(7)     NULL
    ,Number      decimal(18, 6)   NULL
    ,Bigint      bigint           NULL
    ,Char        varchar(4000)    NULL
    ,Binary      varbinary(max)   NULL
    
    ,UpdatedDate datetime2(7)     NULL
    ,UpdatedBy   nvarchar(255)    NULL
    
     CONSTRAINT PKC_Parameters_Id PRIMARY KEY CLUSTERED (Id)
  )

CREATE TABLE dbo.ParametersHistory 
  (
     ChangeId    int              NOT NULL IDENTITY(1,1)
    ,Id          varchar(100)     NOT NULL
    ,Date        datetime2(7)     NULL
    ,Number      decimal(18, 6)   NULL
    ,Bigint      bigint           NULL
    ,Char        varchar(4000)    NULL
    ,Binary      varbinary(max)   NULL
    ,UpdatedDate datetime2(7)     NULL
    ,UpdatedBy   nvarchar(255)    NULL
  )
