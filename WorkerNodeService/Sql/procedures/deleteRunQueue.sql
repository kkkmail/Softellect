drop procedure if exists dbo.deleteRunQueue
go


SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

--declare @runQueueId uniqueidentifier
--set @runQueueId = newid()

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
end
go

