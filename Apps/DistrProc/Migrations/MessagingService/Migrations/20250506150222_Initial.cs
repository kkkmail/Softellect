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
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryType_deliveryTypeName",
                table: "DeliveryType",
                column: "deliveryTypeName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Message_deliveryTypeId",
                table: "Message",
                column: "deliveryTypeId");

            migrationBuilder.UpInitial();
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
