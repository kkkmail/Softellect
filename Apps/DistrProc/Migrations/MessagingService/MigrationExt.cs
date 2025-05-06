using Microsoft.EntityFrameworkCore.Migrations;

namespace Softellect.Migrations.MessagingService;

public static class MigrationExt
{
    public static void UpInitial(this MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            drop procedure if exists deleteExpiredMessages;
            SET ANSI_NULLS ON;
            SET QUOTED_IDENTIFIER ON;
            """);

        migrationBuilder.Sql(
            """
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
            end;
            """);

        migrationBuilder.Sql(
            """
            drop procedure if exists deleteMessage;
            SET ANSI_NULLS ON;
            SET QUOTED_IDENTIFIER ON;
            """);

        migrationBuilder.Sql(
            """
            create procedure deleteMessage @messageId uniqueidentifier
            as
            begin
                declare @rowCount int
                set nocount on;

                delete from dbo.Message where messageId = @messageId

                set @rowCount = @@rowcount
                select @rowCount as [RowCount]
            end;
            """);

        migrationBuilder.Sql(
            """
            drop procedure if exists saveMessage;
            SET ANSI_NULLS ON;
            SET QUOTED_IDENTIFIER ON;
            """);

        migrationBuilder.Sql(
            """
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
            END;
            """);

        migrationBuilder.Sql(
            """
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
            where DeliveryType.deliveryTypeId is null;
            """);
    }
}
