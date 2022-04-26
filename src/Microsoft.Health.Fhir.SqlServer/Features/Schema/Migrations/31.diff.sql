/*************************************************************
    This migration adds new constraint on data length of rawresource
**************************************************************/

IF  EXISTS (SELECT * FROM sys.objects WHERE name = N'RawResourceLength' AND type = N'C')
    ALTER TABLE dbo.Resource DROP CONSTRAINT RawResourceLength
GO

ALTER TABLE dbo.Resource WITH NOCHECK
    ADD CONSTRAINT RawResourceLength CHECK (DATALENGTH(RawResource) > 49)

GO



