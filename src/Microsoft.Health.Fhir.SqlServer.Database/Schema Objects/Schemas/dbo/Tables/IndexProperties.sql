-- DROP TABLE IndexProperties
GO
CREATE TABLE dbo.IndexProperties 
  (
     TableName     varchar(100)     NOT NULL
    ,IndexName     varchar(200)     NOT NULL
    ,PropertyName  varchar(100)     NOT NULL
    ,PropertyValue varchar(100)     NOT NULL
    ,CreateDate    datetime         NOT NULL CONSTRAINT DF_IndexProperties_CreateDate DEFAULT getUTCdate()
    
     CONSTRAINT PKC_IndexProperties_TableName_IndexName_PropertyName PRIMARY KEY CLUSTERED (TableName, IndexName, PropertyName)
  )
GO
--INSERT INTO IndexProperties (TableName,IndexName,PropertyName,PropertyValue) 
--  SELECT 'ReferenceSearchParam', 'IX_ReferenceSearchParam_SearchParamId_ReferenceResourceTypeId_ReferenceResourceId_BaseUri_ReferenceResourceVersion', 'DATA_COMPRESSION', 'PAGE'
