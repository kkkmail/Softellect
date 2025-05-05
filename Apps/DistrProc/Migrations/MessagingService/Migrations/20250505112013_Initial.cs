using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Softellect.Migrations.MessagingService.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeliveryType",
                columns: table => new
                {
                    deliveryTypeId = table.Column<int>(type: "int", nullable: false),
                    deliveryTypeName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryType", x => x.deliveryTypeId);
                });

            migrationBuilder.CreateTable(
                name: "Message",
                columns: table => new
                {
                    messageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    senderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    recipientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    messageOrder = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    dataVersion = table.Column<int>(type: "int", nullable: false),
                    deliveryTypeId = table.Column<int>(type: "int", nullable: false),
                    messageData = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    createdOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Message", x => x.messageId);
                    table.ForeignKey(
                        name: "FK_Message_DeliveryType_deliveryTypeId",
                        column: x => x.deliveryTypeId,
                        principalTable: "DeliveryType",
                        principalColumn: "deliveryTypeId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Message_deliveryTypeId",
                table: "Message",
                column: "deliveryTypeId");

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Message");

            migrationBuilder.DropTable(
                name: "DeliveryType");
        }
    }
}
