using Microsoft.EntityFrameworkCore.Migrations;

namespace Softellect.Migrations.WorkerNodeService;

public static class MigrationExt
{
    public static void UpInitial(this MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            drop function if exists dbo.RunQueueStatus_NotStarted;
            drop function if exists dbo.RunQueueStatus_Inactive;
            drop function if exists dbo.RunQueueStatus_RunRequested;
            drop function if exists dbo.RunQueueStatus_InProgress;
            drop function if exists dbo.RunQueueStatus_Completed;
            drop function if exists dbo.RunQueueStatus_Failed;
            drop function if exists dbo.RunQueueStatus_CancelRequested;
            drop function if exists dbo.RunQueueStatus_Cancelled;
            drop procedure if exists dbo.deleteRunQueue;
            drop procedure if exists dbo.tryCancelRunQueue;
            drop procedure if exists dbo.tryClearNotificationRunQueue;
            drop procedure if exists dbo.tryCompleteRunQueue;
            drop procedure if exists dbo.tryFailRunQueue;
            drop procedure if exists dbo.tryNotifyRunQueue;
            drop procedure if exists dbo.tryRequestCancelRunQueue;
            drop procedure if exists dbo.tryStartRunQueue;
            drop procedure if exists dbo.tryUpdateProgressRunQueue;
            drop procedure if exists deleteExpiredMessages;
            drop procedure if exists deleteMessage;
            drop procedure if exists saveMessage;
            
            SET ANSI_NULLS ON;
            SET QUOTED_IDENTIFIER ON;
            """);

        migrationBuilder.Sql(
            """
            create function dbo.RunQueueStatus_NotStarted() returns int as begin return 0 end;
            """);

        migrationBuilder.Sql(
            """
            create function dbo.RunQueueStatus_Inactive() returns int as begin return 1 end;
            """);

        migrationBuilder.Sql(
            """
            create function dbo.RunQueueStatus_RunRequested() returns int as begin return 7 end;
            """);

        migrationBuilder.Sql(
            """
            create function dbo.RunQueueStatus_InProgress() returns int as begin return 2 end;
            """);

        migrationBuilder.Sql(
            """
            create function dbo.RunQueueStatus_Completed() returns int as begin return 3 end;
            """);

        migrationBuilder.Sql(
            """
            create function dbo.RunQueueStatus_Failed() returns int as begin return 4 end;
            """);

        migrationBuilder.Sql(
            """
            create function dbo.RunQueueStatus_CancelRequested() returns int as begin return 5 end;
            """);

        migrationBuilder.Sql(
            """
            create function dbo.RunQueueStatus_Cancelled() returns int as begin return 6 end;
            """);

        migrationBuilder.Sql(
            """
            create procedure dbo.deleteRunQueue @runQueueId uniqueidentifier
            as
            begin
                set nocount on;

                declare @rowCount int;
                declare @sql nvarchar(max);

                -- Generate the delete statements for all tables with a foreign key referencing dbo.RunQueue.runQueueId
                select @sql = STRING_AGG(
                    'delete from ' + QUOTENAME(OBJECT_SCHEMA_NAME(fk.parent_object_id)) + '.' + QUOTENAME(OBJECT_NAME(fk.parent_object_id)) + ' where runQueueId = @runQueueId;', 
                    CHAR(13) + CHAR(10)
                )
                from 
                    sys.foreign_keys as fk
                    inner join sys.foreign_key_columns as fkc
                        on fk.object_id = fkc.constraint_object_id
                    inner join sys.columns as c
                        on fkc.parent_column_id = c.column_id and fkc.parent_object_id = c.object_id
                    inner join sys.tables as t
                        on fk.parent_object_id = t.object_id
                where 
                    fk.referenced_object_id = object_id('dbo.RunQueue')
                    and c.name = 'runQueueId';

                -- Execute the dynamic SQL if there are any delete statements
                if @sql is not null and @sql <> ''
                begin
                    --print @sql
                    exec sp_executesql @sql, N'@runQueueId uniqueidentifier', @runQueueId;
                end

                -- Delete from dbo.RunQueue and capture the row count
                delete from dbo.RunQueue where runQueueId = @runQueueId;
                set @rowCount = @@rowcount;

                -- Return the number of rows affected by the delete from dbo.RunQueue
                select @rowCount as [RowCount];
            end;
            """);

        migrationBuilder.Sql(
            """
            create procedure dbo.tryCancelRunQueue (@runQueueId uniqueidentifier, @errorMessage nvarchar(max) = null)
            as
            begin
               declare @rowCount int
               set nocount on;

               update dbo.RunQueue
               set
                  runQueueStatusId = dbo.RunQueueStatus_Cancelled(),
                  processId = null,
                  modifiedOn = (getdate()),
                  errorMessage = @errorMessage
               where runQueueId = @runQueueId and runQueueStatusId in (dbo.RunQueueStatus_NotStarted(), dbo.RunQueueStatus_InProgress(), dbo.RunQueueStatus_CancelRequested())

               set @rowCount = @@rowcount
               select @rowCount as [RowCount]
            end;
            """);

        migrationBuilder.Sql(
            """
            create procedure dbo.tryClearNotificationRunQueue @runQueueId uniqueidentifier
            as
            begin
               declare @rowCount int
               set nocount on;

                update dbo.RunQueue
                set
                    notificationTypeId = 0,
                    modifiedOn = (getdate())
                where runQueueId = @runQueueId and runQueueStatusId in (dbo.RunQueueStatus_InProgress())

               set @rowCount = @@rowcount
               select @rowCount as [RowCount]
            end;

            """);

        migrationBuilder.Sql(
            """
            create procedure dbo.tryCompleteRunQueue @runQueueId uniqueidentifier
            as
            begin
               declare @rowCount int
               set nocount on;

               update dbo.RunQueue
               set
                  runQueueStatusId = dbo.RunQueueStatus_Completed(),
                  processId = null,
                  modifiedOn = (getdate())
               where runQueueId = @runQueueId and processId is not null and runQueueStatusId in (dbo.RunQueueStatus_InProgress(), dbo.RunQueueStatus_CancelRequested())

               set @rowCount = @@rowcount
               select @rowCount as [RowCount]
            end;

            """);

        migrationBuilder.Sql(
            """
            create procedure dbo.tryFailRunQueue (@runQueueId uniqueidentifier, @errorMessage nvarchar(max) = null)
            as
            begin
               declare @rowCount int
               set nocount on;

                update dbo.RunQueue
                set
                    runQueueStatusId = dbo.RunQueueStatus_Failed(),
                    processId = null,
                    modifiedOn = (getdate()),
                    errorMessage = @errorMessage
                where runQueueId = @runQueueId and runQueueStatusId in (dbo.RunQueueStatus_InProgress(), dbo.RunQueueStatus_CancelRequested())

               set @rowCount = @@rowcount
               select @rowCount as [RowCount]
            end;

            """);

        migrationBuilder.Sql(
            """
            create procedure dbo.tryNotifyRunQueue (@runQueueId uniqueidentifier, @notificationTypeId int)
            as
            begin
               declare @rowCount int
               set nocount on;

                update dbo.RunQueue
                set
                    notificationTypeId = @notificationTypeId,
                    modifiedOn = (getdate())
                where runQueueId = @runQueueId and runQueueStatusId in (dbo.RunQueueStatus_InProgress(), dbo.RunQueueStatus_CancelRequested())

               set @rowCount = @@rowcount
               select @rowCount as [RowCount]
            end;

            """);

        migrationBuilder.Sql(
            """
            create procedure dbo.tryRequestCancelRunQueue (@runQueueId uniqueidentifier, @notificationTypeId int)
            as
            begin
               declare @rowCount int
               set nocount on;

                update dbo.RunQueue
                set
                    runQueueStatusId = dbo.RunQueueStatus_CancelRequested(),
                    notificationTypeId = @notificationTypeId,
                    modifiedOn = (getdate())
                where runQueueId = @runQueueId and runQueueStatusId = dbo.RunQueueStatus_InProgress()

               set @rowCount = @@rowcount
               select @rowCount as [RowCount]
            end;

            """);

        migrationBuilder.Sql(
            """
            create procedure dbo.tryStartRunQueue (@runQueueId uniqueidentifier, @processId int)
            as
            begin
               declare @rowCount int
               set nocount on;

               update dbo.RunQueue
               set
                  processId = @processId,
                  runQueueStatusId = dbo.RunQueueStatus_InProgress(),
                  startedOn = (getdate()),
                  modifiedOn = (getdate())
               where runQueueId = @runQueueId and runQueueStatusId in (dbo.RunQueueStatus_NotStarted(), dbo.RunQueueStatus_InProgress())


               set @rowCount = @@rowcount
               select @rowCount as [RowCount]
            end;

            """);

        migrationBuilder.Sql(
            """
            create procedure dbo.tryUpdateProgressRunQueue (
                                    @runQueueId uniqueidentifier,
                                    @progress decimal(38, 16),
                                    @evolutionTime decimal(38, 16),
                                    @progressData nvarchar(max) = null,
                                    @callCount bigint,
                                    @relativeInvariant float)
            as
            begin
               declare @rowCount int
               set nocount on;

                update dbo.RunQueue
                set
                    progress = @progress,
                    evolutionTime = @evolutionTime,
                    progressData = @progressData,
                    callCount = @callCount,
                    relativeInvariant = @relativeInvariant,
                    modifiedOn = (getdate())
                where runQueueId = @runQueueId and runQueueStatusId in (dbo.RunQueueStatus_InProgress(), dbo.RunQueueStatus_CancelRequested())

               set @rowCount = @@rowcount
               select @rowCount as [RowCount]
            end;

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
                       (0, 'NoChartGeneration')
                     , (1, 'RegularChartGeneration')
                     , (2, 'ForceChartGeneration')
                  ) as a (notificationTypeId, notificationTypeName)
               )
            insert into NotificationType
            select valTbl.*
            from valTbl
            left outer join NotificationType on valTbl.notificationTypeId = NotificationType.notificationTypeId
            where NotificationType.notificationTypeId is null;
            """);

        migrationBuilder.Sql(
            """
            ;with 
               valTbl as
               (
                  select * 
                  from 
                  ( values
                       (0, 'NotStarted')
                     , (1, 'Inactive')
                     , (2, 'InProgress')
                     , (3, 'Completed')
                     , (4, 'Failed')
                     , (5, 'CancelRequested')
                     , (6, 'Cancelled')

                  ) as a (runQueueStatusId, runQueueStatusName)
               )
            insert into RunQueueStatus
            select valTbl.*
            from valTbl
            left outer join RunQueueStatus on valTbl.runQueueStatusId = RunQueueStatus.runQueueStatusId
            where RunQueueStatus.runQueueStatusId is null;
            """);

        migrationBuilder.Sql(
            """
            ;with 
               valTbl as
               (
                  select * 
                  from 
                  ( values
                       ('Suspended', 0, NULL, NULL, NULL, NULL, getdate())

                  ) as a (settingName, settingBool, settingGuid, settingLong, settingText, settingBinary, createdOn)
               )
            insert into Setting
            select valTbl.*
            from valTbl
            left outer join Setting on valTbl.settingName = Setting.settingName
            where Setting.settingName is null;
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
