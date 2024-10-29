IF OBJECT_ID('dbo.DeliveryType') IS NULL begin
	print 'Creating table dbo.DeliveryType ...'

	CREATE TABLE dbo.DeliveryType(
		deliveryTypeId int NOT NULL,
		deliveryTypeName nvarchar(50) NOT NULL,
	 CONSTRAINT PK_DeliveryType PRIMARY KEY CLUSTERED 
	(
		deliveryTypeId ASC
	) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
	) ON [PRIMARY]
end else begin
	print 'Table dbo.DeliveryType already exists ...'
end
go

IF OBJECT_ID('dbo.Message') IS NULL begin
	print 'Creating table dbo.Message ...'

	CREATE TABLE dbo.Message(
		messageId uniqueidentifier NOT NULL,
		senderId uniqueidentifier NOT NULL,
		recipientId uniqueidentifier NOT NULL,
		messageOrder bigint IDENTITY(1,1) NOT NULL,
		dataVersion int NOT NULL,
		deliveryTypeId int NOT NULL,
		messageData varbinary(max) NOT NULL,
		createdOn datetime NOT NULL,
	 CONSTRAINT PK_Message PRIMARY KEY CLUSTERED 
	(
		messageId ASC
	) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
	) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

	ALTER TABLE dbo.Message  WITH CHECK ADD  CONSTRAINT FK_Message_DeliveryType FOREIGN KEY(deliveryTypeId)
	REFERENCES dbo.DeliveryType (deliveryTypeId)

	ALTER TABLE dbo.Message CHECK CONSTRAINT FK_Message_DeliveryType
end else begin
	print 'Table dbo.Message already exists ...'
end
go

drop procedure if exists deleteExpiredMessages
go


SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO


create procedure deleteExpiredMessages (@dataVersion int, @createdOn datetime)
as
begin
	declare @rowCount int
	set nocount on;

    delete from dbo.Message
    where
        deliveryTypeId = 1
        and dataVersion = @dataVersion
        and createdOn < @createdOn

	set @rowCount = @@rowcount
	select @rowCount as [RowCount]
end
go

drop procedure if exists deleteMessage
go


SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO


create procedure deleteMessage @messageId uniqueidentifier
as
begin
	declare @rowCount int
	set nocount on;

	delete from dbo.Message where messageId = @messageId

	set @rowCount = @@rowcount
	select @rowCount as [RowCount]
end
go

drop procedure if exists saveMessage
go


SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO


--create procedure saveMessage (
--					@messageId uniqueidentifier,
--					@senderId uniqueidentifier,
--					@recipientId uniqueidentifier,
--					@dataVersion int,
--					@deliveryTypeId int,
--					@messageData varbinary(max))
--as
--begin
--	declare @rowCount int
--	set nocount on;

--	insert into Message (messageId, senderId, recipientId, dataVersion, deliveryTypeId, messageData, createdOn)
--	select @messageId, @senderId, @recipientId, @dataVersion, @deliveryTypeId, @messageData, getdate()
--	where not exists (select 1 from Message where messageId = @messageId)

--	set @rowCount = @@rowcount
--	select @rowCount as [RowCount]
--end
--go

--CREATE PROCEDURE saveMessage (
--    @messageId uniqueidentifier,
--    @senderId uniqueidentifier,
--    @recipientId uniqueidentifier,
--    @dataVersion int,
--    @deliveryTypeId int,
--    @messageData varbinary(max)
--)
--AS
--BEGIN
--    DECLARE @rowCount int
--    SET NOCOUNT ON;

--    BEGIN TRY
--        -- Attempt to insert the row if it doesn’t already exist
--        INSERT INTO Message (messageId, senderId, recipientId, dataVersion, deliveryTypeId, messageData, createdOn)
--        SELECT @messageId, @senderId, @recipientId, @dataVersion, @deliveryTypeId, @messageData, GETDATE()
--        WHERE NOT EXISTS (SELECT 1 FROM Message WHERE messageId = @messageId)

--        -- Capture the row count after insertion
--        SET @rowCount = @@ROWCOUNT
--        SELECT @rowCount AS [RowCount]
--    END TRY
--    BEGIN CATCH
--        -- Capture and return error details if any
--        DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
--        DECLARE @ErrorSeverity INT = ERROR_SEVERITY();
--        DECLARE @ErrorState INT = ERROR_STATE();
--        RAISERROR(@ErrorMessage, @ErrorSeverity, @ErrorState);
--        RETURN -1  -- Optional: indicate error with a negative row count
--    END CATCH
--END
--GO

CREATE PROCEDURE saveMessage (
    @messageId uniqueidentifier,
    @senderId uniqueidentifier,
    @recipientId uniqueidentifier,
    @dataVersion int,
    @deliveryTypeId int,
    @messageData varbinary(max)
)
AS
BEGIN
    DECLARE @rowCount int
    SET NOCOUNT ON;

    -- Check if the message already exists
    IF NOT EXISTS (SELECT 1 FROM Message WHERE messageId = @messageId)
    BEGIN
        -- If not, insert it and set row count to 1
        INSERT INTO Message (messageId, senderId, recipientId, dataVersion, deliveryTypeId, messageData, createdOn)
        VALUES (@messageId, @senderId, @recipientId, @dataVersion, @deliveryTypeId, @messageData, GETDATE())

        SET @rowCount = 1  -- Indicate a successful insert
    END
    ELSE
    BEGIN
        -- If already exists, set row count to 0 without inserting
        SET @rowCount = 0
    END

    -- Return the row count to indicate whether an insertion occurred
    SELECT @rowCount AS [RowCount]
END
GO

;with 
	valTbl as
	(
		select * 
		from 
		( values
			  (0, 'GuaranteedDelivery')
			, (1, 'NonGuaranteedDelivery')

		) as a (deliveryTypeId, deliveryTypeName)
	)
insert into DeliveryType
select valTbl.*
from valTbl
left outer join DeliveryType on valTbl.deliveryTypeId = DeliveryType.deliveryTypeId
where DeliveryType.deliveryTypeId is null
go


