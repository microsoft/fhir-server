--IF object_id('GetIndexCommands') IS NOT NULL DROP PROCEDURE dbo.GetIndexCommands
GO
CREATE OR ALTER PROCEDURE dbo.GetIndexCommands @Tbl varchar(100), @Ind varchar(200), @AddPartClause bit, @IncludeClustered bit, @Txt varchar(max) = NULL OUT
WITH EXECUTE AS 'dbo'
AS
set nocount on
DECLARE @SP varchar(100) = 'GetIndexCommands'
      ,@Mode varchar(200) = 'Tbl='+isnull(@Tbl,'NULL')+' Ind='+isnull(@Ind,'NULL')
      ,@st datetime = getUTCdate()
Declare @KeyColsTable Table(KeyCol varchar(200))
Declare @IncColsTable Table(IncCol varchar(200))
Declare @Table_index Table(object_id int, index_id int)
Declare @DataComp varchar(100)
Declare @FilterDef varchar(200),@CommandForKey varchar(200),@CommandForInc varchar(200),@TblId int, @IndId int, @colname varchar(100)
Declare @PartClause varchar(100)
DECLARE @Indexes TABLE (Ind varchar(200) PRIMARY KEY, Txt varchar(max))
Declare @Temp Table(object_id int, index_id int, KeyCols varchar(200), IncCols varchar(200))

BEGIN TRY
 IF @Tbl IS NULL RAISERROR('@Tbl IS NULL',18,127)

insert into @Table_index
select I.object_id, I.index_id from sys.indexes I join sys.objects O on I.object_id=O.object_id where O.name=@Tbl and I.name=@Ind

while Exists (select * from @Table_index)
BEGIN
select top 1 @TblId = object_id, @IndId = index_id from @Table_index
set @CommandForKey = ''
set @CommandForInc = ''
Delete @KeyColsTable
insert into @KeyColsTable
select C.name
from sys.index_columns IC join sys.indexes I on IC.object_id=I.object_id and IC.index_id=I.index_id, sys.columns C
where C.column_id = IC.column_id and C.object_id=IC.object_id and IC.object_id=@TblId and IC.index_id=@IndId and IC.key_ordinal > 0 AND IC.is_included_column = 0 order by key_ordinal
while Exists (select * from @KeyColsTable)
BEGIN
select top 1 @colname=KeyCol from @KeyColsTable
set @CommandForKey = @CommandForKey+@colname+','
Delete from @KeyColsTable where KeyCol=@colname
END
Delete @IncColsTable
insert into @IncColsTable
select C.name
from sys.index_columns IC join sys.indexes I on IC.object_id=I.object_id and IC.index_id=I.index_id, sys.columns C
where C.column_id = IC.column_id and C.object_id=IC.object_id and IC.object_id=@TblId and IC.index_id=@IndId AND IC.is_included_column = 1 order by key_ordinal
while Exists(select * from @IncColsTable)
BEGIN
select top 1 @colname = IncCol from @IncColsTable
set @CommandForInc = @CommandForInc+@colname+','
Delete from @IncColsTable where IncCol=@colname
END
set @DataComp=isnull((SELECT TOP 1 CASE WHEN data_compression_desc = 'PAGE' THEN 'PAGE' END FROM sys.partitions P WHERE P.object_id = @TblId AND P.index_id = @IndId)
                                    ,(SELECT top 1 nullif(PropertyValue,'NONE') FROM dbo.IndexProperties, sys.objects O, sys.indexes I WHERE TableN = O.Name AND IndexName = I.Name AND O.name=@Tbl AND I.name=@Ind AND PropertyName = 'DATA_COMPRESSION')
                                    )
select @FilterDef = replace(replace(replace(replace(I.filter_definition,'[',''),']',''),'(',''),')','') from sys.indexes I where I.object_id=@TblId and I.index_id=@IndId
select @PartClause = CASE WHEN EXISTS (SELECT * FROM sys.partition_schemes S, sys.indexes I WHERE S.data_space_id = I.data_space_id AND S.name = 'PartitionScheme_ResourceTypeId'AND I.object_id=@TblId AND I.name=@Ind)  THEN ' ON PartitionScheme_ResourceTypeId (ResourceTypeId)' ELSE '' END
insert into @Indexes
select Ind=@Ind, Txt=CASE
            WHEN is_primary_key = 1
              THEN 'ALTER TABLE dbo.['+@Tbl+'] ADD PRIMARY KEY '+CASE WHEN I.type = 1 THEN ' CLUSTERED' ELSE '' END -- Skip PK name, then this string can be applied to all component tables with no changes.
            ELSE 'CREATE'+CASE WHEN is_unique = 1 THEN ' UNIQUE' ELSE '' END+CASE WHEN I.type = 1 THEN ' CLUSTERED' ELSE '' END+' INDEX '+@Ind+' ON dbo.['+@Tbl+']'
          END
         +' ('+left(@CommandForKey, len(@CommandForKey)-1)+')'
         +CASE WHEN @CommandForInc <> '' THEN ' INCLUDE ('+left(@CommandForInc, len(@CommandForInc)-1)+')' ELSE '' END
         +CASE WHEN @FilterDef IS NOT NULL THEN ' WHERE '+@FilterDef ELSE '' END
         +CASE WHEN @DataComp IS NOT NULL THEN ' WITH (DATA_COMPRESSION = '+@DataComp+')' ELSE '' END
         +CASE WHEN @AddPartClause=1 THEN @PartClause ELSE '' END
 From sys.indexes I join sys.objects O on I.object_id=O.object_id
 where O.object_id=@TblId and I.index_id=@IndId and (@IncludeClustered=1 OR index_id>1)

 Delete from @Table_index where object_id=@TblId
END
 EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='@Indexes',@Action='Insert',@Rows=@@rowcount

 IF @Ind IS NULL -- return records
   SELECT Ind, Txt FROM @Indexes
 ELSE
   SET @Txt = (SELECT Txt FROM @Indexes) -- There should be only one record

 EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Text=@Txt
END TRY
BEGIN CATCH
 IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
 EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
 THROW
END CATCH
GO
