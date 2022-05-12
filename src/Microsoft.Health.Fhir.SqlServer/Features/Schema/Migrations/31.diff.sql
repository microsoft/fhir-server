/*************************************************************
    This migration adds new constraint on data length of rawresource
**************************************************************/

IF  EXISTS (SELECT * FROM sys.objects WHERE name = N'CH_Resource_RawResource_Length' AND type = N'C')
    ALTER TABLE dbo.Resource DROP CONSTRAINT CH_Resource_RawResource_Length
GO

ALTER TABLE dbo.Resource WITH NOCHECK
    ADD CONSTRAINT CH_Resource_RawResource_Length CHECK (DATALENGTH(RawResource) > 0)

GO



