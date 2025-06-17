SET XACT_ABORT ON
BEGIN TRANSACTION

IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name = 'CHK_ReferenceTokenCompositeSearchParam_CodeOverflow2')
BEGIN    
    ALTER TABLE dbo.ReferenceTokenCompositeSearchParam Drop CONSTRAINT CHK_ReferenceTokenCompositeSearchParam_CodeOverflow2;
    ALTER TABLE dbo.ReferenceTokenCompositeSearchParam
    ADD CONSTRAINT CHK_ReferenceTokenCompositeSearchParam_CodeOverflow2 CHECK (DATALENGTH(Code2) = 256
                                                                               OR CodeOverflow2 IS NULL);
END
COMMIT TRANSACTION
GO

BEGIN TRANSACTION
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name = 'CHK_TokenDateTimeCompositeSearchParam_CodeOverflow1')
BEGIN    
    ALTER TABLE dbo.TokenDateTimeCompositeSearchParam Drop CONSTRAINT CHK_TokenDateTimeCompositeSearchParam_CodeOverflow1;
    ALTER TABLE dbo.TokenDateTimeCompositeSearchParam
    ADD CONSTRAINT CHK_TokenDateTimeCompositeSearchParam_CodeOverflow1 CHECK (DATALENGTH(Code1) = 256
                                                                              OR CodeOverflow1 IS NULL);
END
COMMIT TRANSACTION
GO

BEGIN TRANSACTION
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name = 'CHK_TokenNumberNumberCompositeSearchParam_CodeOverflow1')
BEGIN    
    ALTER TABLE dbo.TokenNumberNumberCompositeSearchParam Drop CONSTRAINT CHK_TokenNumberNumberCompositeSearchParam_CodeOverflow1;
    ALTER TABLE dbo.TokenNumberNumberCompositeSearchParam
    ADD CONSTRAINT CHK_TokenNumberNumberCompositeSearchParam_CodeOverflow1 CHECK (DATALENGTH(Code1) = 256
                                                                                  OR CodeOverflow1 IS NULL);
END
COMMIT TRANSACTION
GO

BEGIN TRANSACTION
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name = 'CHK_TokenQuantityCompositeSearchParam_CodeOverflow1')
BEGIN    
    ALTER TABLE dbo.TokenQuantityCompositeSearchParam Drop CONSTRAINT CHK_TokenQuantityCompositeSearchParam_CodeOverflow1;
    ALTER TABLE dbo.TokenQuantityCompositeSearchParam
    ADD CONSTRAINT CHK_TokenQuantityCompositeSearchParam_CodeOverflow1 CHECK (DATALENGTH(Code1) = 256
                                                                              OR CodeOverflow1 IS NULL);
END
COMMIT TRANSACTION
GO

BEGIN TRANSACTION
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name = 'CHK_TokenStringCompositeSearchParam_CodeOverflow1')
BEGIN    
    ALTER TABLE dbo.TokenStringCompositeSearchParam Drop CONSTRAINT CHK_TokenStringCompositeSearchParam_CodeOverflow1;
    ALTER TABLE dbo.TokenStringCompositeSearchParam
    ADD CONSTRAINT CHK_TokenStringCompositeSearchParam_CodeOverflow1 CHECK (DATALENGTH(Code1) = 256
                                                                            OR CodeOverflow1 IS NULL);
END
COMMIT TRANSACTION
GO

BEGIN TRANSACTION
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name = 'CHK_TokenTokenCompositeSearchParam_CodeOverflow1')
BEGIN    
    ALTER TABLE dbo.TokenTokenCompositeSearchParam Drop CONSTRAINT CHK_TokenTokenCompositeSearchParam_CodeOverflow1;
    ALTER TABLE dbo.TokenTokenCompositeSearchParam
    ADD CONSTRAINT CHK_TokenTokenCompositeSearchParam_CodeOverflow1 CHECK (DATALENGTH(Code1) = 256
                                                                           OR CodeOverflow1 IS NULL);
END
COMMIT TRANSACTION
GO

BEGIN TRANSACTION
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name = 'CHK_TokenTokenCompositeSearchParam_CodeOverflow2')
BEGIN    
    ALTER TABLE dbo.TokenTokenCompositeSearchParam Drop CONSTRAINT CHK_TokenTokenCompositeSearchParam_CodeOverflow2;
    ALTER TABLE dbo.TokenTokenCompositeSearchParam
    ADD CONSTRAINT CHK_TokenTokenCompositeSearchParam_CodeOverflow2 CHECK (DATALENGTH(Code2) = 256
                                                                           OR CodeOverflow2 IS NULL);
END
COMMIT TRANSACTION
GO

BEGIN TRANSACTION
IF EXISTS(SELECT 1 FROM sys.check_constraints WHERE name = 'CHK_TokenSearchParam_CodeOverflow')
BEGIN    
    ALTER TABLE dbo.TokenSearchParam Drop CONSTRAINT CHK_TokenSearchParam_CodeOverflow;
    ALTER TABLE dbo.TokenSearchParam
    ADD CONSTRAINT CHK_TokenSearchParam_CodeOverflow CHECK (DATALENGTH(Code) = 256
                                                            OR CodeOverflow IS NULL);
END
COMMIT TRANSACTION
GO
