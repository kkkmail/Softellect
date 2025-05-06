using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Softellect.Migrations.WorkerNodeService.Migrations
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
                name: "NotificationType",
                columns: table => new
                {
                    notificationTypeId = table.Column<int>(type: "int", nullable: false),
                    notificationTypeName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationType", x => x.notificationTypeId);
                });

            migrationBuilder.CreateTable(
                name: "RunQueueStatus",
                columns: table => new
                {
                    runQueueStatusId = table.Column<int>(type: "int", nullable: false),
                    runQueueStatusName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunQueueStatus", x => x.runQueueStatusId);
                });

            migrationBuilder.CreateTable(
                name: "Setting",
                columns: table => new
                {
                    settingName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    settingBool = table.Column<bool>(type: "bit", nullable: true),
                    settingGuid = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    settingLong = table.Column<long>(type: "bigint", nullable: true),
                    settingText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    settingBinary = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    createdOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Setting", x => x.settingName);
                });

            migrationBuilder.CreateTable(
                name: "Solver",
                columns: table => new
                {
                    solverId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    solverOrder = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    solverName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    solverData = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    createdOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    isDeployed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Solver", x => x.solverId);
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

            migrationBuilder.CreateTable(
                name: "RunQueue",
                columns: table => new
                {
                    runQueueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    runQueueOrder = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    solverId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    runQueueStatusId = table.Column<int>(type: "int", nullable: false),
                    processId = table.Column<int>(type: "int", nullable: true),
                    notificationTypeId = table.Column<int>(type: "int", nullable: false),
                    errorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    lastErrorOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    retryCount = table.Column<int>(type: "int", nullable: false),
                    maxRetries = table.Column<int>(type: "int", nullable: false),
                    progress = table.Column<decimal>(type: "decimal(38,16)", precision: 38, scale: 16, nullable: false),
                    progressData = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    callCount = table.Column<long>(type: "bigint", nullable: false),
                    evolutionTime = table.Column<decimal>(type: "decimal(38,16)", precision: 38, scale: 16, nullable: false),
                    relativeInvariant = table.Column<float>(type: "real", nullable: false),
                    createdOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    startedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    modifiedOn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunQueue", x => x.runQueueId);
                    table.ForeignKey(
                        name: "FK_RunQueue_NotificationType_notificationTypeId",
                        column: x => x.notificationTypeId,
                        principalTable: "NotificationType",
                        principalColumn: "notificationTypeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RunQueue_RunQueueStatus_runQueueStatusId",
                        column: x => x.runQueueStatusId,
                        principalTable: "RunQueueStatus",
                        principalColumn: "runQueueStatusId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RunQueue_Solver_solverId",
                        column: x => x.solverId,
                        principalTable: "Solver",
                        principalColumn: "solverId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ModelData",
                columns: table => new
                {
                    runQueueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    modelData = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelData", x => x.runQueueId);
                    table.ForeignKey(
                        name: "FK_ModelData_RunQueue_runQueueId",
                        column: x => x.runQueueId,
                        principalTable: "RunQueue",
                        principalColumn: "runQueueId",
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

            migrationBuilder.CreateIndex(
                name: "IX_NotificationType_notificationTypeName",
                table: "NotificationType",
                column: "notificationTypeName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunQueue_notificationTypeId",
                table: "RunQueue",
                column: "notificationTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_RunQueue_runQueueStatusId",
                table: "RunQueue",
                column: "runQueueStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_RunQueue_solverId",
                table: "RunQueue",
                column: "solverId");

            migrationBuilder.CreateIndex(
                name: "IX_RunQueueStatus_runQueueStatusName",
                table: "RunQueueStatus",
                column: "runQueueStatusName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Setting_settingName",
                table: "Setting",
                column: "settingName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Solver_solverName",
                table: "Solver",
                column: "solverName",
                unique: true);

            migrationBuilder.UpManual();
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Message");

            migrationBuilder.DropTable(
                name: "ModelData");

            migrationBuilder.DropTable(
                name: "Setting");

            migrationBuilder.DropTable(
                name: "DeliveryType");

            migrationBuilder.DropTable(
                name: "RunQueue");

            migrationBuilder.DropTable(
                name: "NotificationType");

            migrationBuilder.DropTable(
                name: "RunQueueStatus");

            migrationBuilder.DropTable(
                name: "Solver");
        }
    }
}
