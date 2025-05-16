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


