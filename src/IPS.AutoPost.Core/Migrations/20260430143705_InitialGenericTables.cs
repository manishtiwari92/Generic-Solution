using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IPS.AutoPost.Core.Migrations
{
    /// <inheritdoc />
    public partial class InitialGenericTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "generic_feed_download_history",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    job_config_id = table.Column<int>(type: "int", nullable: false),
                    feed_name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    is_manual = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    status = table.Column<string>(type: "VARCHAR(20)", nullable: false),
                    record_count = table.Column<int>(type: "int", nullable: true),
                    error_message = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    download_date = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_generic_feed_download_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "generic_job_configuration",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    client_type = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    job_id = table.Column<int>(type: "int", nullable: false),
                    job_name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    default_user_id = table.Column<int>(type: "int", nullable: false, defaultValue: 100),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    source_queue_id = table.Column<string>(type: "VARCHAR(500)", nullable: false),
                    success_queue_id = table.Column<int>(type: "int", nullable: true),
                    primary_fail_queue_id = table.Column<int>(type: "int", nullable: true),
                    secondary_fail_queue_id = table.Column<int>(type: "int", nullable: true),
                    question_queue_id = table.Column<int>(type: "int", nullable: true),
                    terminated_queue_id = table.Column<int>(type: "int", nullable: true),
                    header_table = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    detail_table = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    detail_uid_column = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    history_table = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    db_connection_string = table.Column<string>(type: "NVARCHAR(500)", nullable: true),
                    post_service_url = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    auth_type = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    auth_username = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    auth_password = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    last_post_time = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_download_time = table.Column<DateTime>(type: "datetime2", nullable: true),
                    allow_auto_post = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    download_feed = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    output_file_path = table.Column<string>(type: "NVARCHAR(500)", nullable: true),
                    feed_download_path = table.Column<string>(type: "NVARCHAR(500)", nullable: true),
                    image_parent_path = table.Column<string>(type: "NVARCHAR(500)", nullable: true),
                    new_ui_image_parent_path = table.Column<string>(type: "NVARCHAR(500)", nullable: true),
                    is_legacy_job = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    client_config_json = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    created_date = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()"),
                    modified_date = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_generic_job_configuration", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "generic_post_history",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    client_type = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    job_id = table.Column<int>(type: "int", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    step_name = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    post_request = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    post_response = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    post_date = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()"),
                    posted_by = table.Column<int>(type: "int", nullable: false),
                    manually_posted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    output_file_path = table.Column<string>(type: "NVARCHAR(500)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_generic_post_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "generic_auth_configuration",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    job_config_id = table.Column<int>(type: "int", nullable: false),
                    auth_purpose = table.Column<string>(type: "VARCHAR(20)", nullable: false),
                    auth_key = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    auth_type = table.Column<string>(type: "VARCHAR(20)", nullable: false),
                    username = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    password = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    api_key = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    token_url = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    secret_arn = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    extra_json = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_generic_auth_configuration", x => x.id);
                    table.ForeignKey(
                        name: "FK_generic_auth_configuration_generic_job_configuration_job_config_id",
                        column: x => x.job_config_id,
                        principalTable: "generic_job_configuration",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "generic_email_configuration",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    job_config_id = table.Column<int>(type: "int", nullable: false),
                    email_type = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    email_to = table.Column<string>(type: "NVARCHAR(1000)", nullable: true),
                    email_cc = table.Column<string>(type: "NVARCHAR(1000)", nullable: true),
                    email_bcc = table.Column<string>(type: "NVARCHAR(1000)", nullable: true),
                    email_subject = table.Column<string>(type: "NVARCHAR(500)", nullable: true),
                    email_template = table.Column<string>(type: "NVARCHAR(500)", nullable: true),
                    smtp_server = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    smtp_port = table.Column<int>(type: "int", nullable: true, defaultValue: 587),
                    smtp_username = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    smtp_password = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    smtp_use_ssl = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_generic_email_configuration", x => x.id);
                    table.ForeignKey(
                        name: "FK_generic_email_configuration_generic_job_configuration_job_config_id",
                        column: x => x.job_config_id,
                        principalTable: "generic_job_configuration",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "generic_execution_history",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    job_config_id = table.Column<int>(type: "int", nullable: false),
                    client_type = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    job_id = table.Column<int>(type: "int", nullable: false),
                    execution_type = table.Column<string>(type: "VARCHAR(30)", nullable: false),
                    trigger_type = table.Column<string>(type: "VARCHAR(20)", nullable: false),
                    status = table.Column<string>(type: "VARCHAR(20)", nullable: false),
                    records_processed = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    records_succeeded = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    records_failed = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    error_details = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    start_time = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()"),
                    end_time = table.Column<DateTime>(type: "datetime2", nullable: true),
                    triggered_by_user = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_generic_execution_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_generic_execution_history_generic_job_configuration_job_config_id",
                        column: x => x.job_config_id,
                        principalTable: "generic_job_configuration",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "generic_execution_schedule",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    job_config_id = table.Column<int>(type: "int", nullable: false),
                    schedule_type = table.Column<string>(type: "VARCHAR(20)", nullable: false, defaultValue: "POST"),
                    execution_time = table.Column<string>(type: "VARCHAR(10)", nullable: true),
                    cron_expression = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    last_execution_time = table.Column<DateTime>(type: "datetime2", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_generic_execution_schedule", x => x.id);
                    table.CheckConstraint("CHK_schedule_has_time", "execution_time IS NOT NULL OR cron_expression IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_generic_execution_schedule_generic_job_configuration_job_config_id",
                        column: x => x.job_config_id,
                        principalTable: "generic_job_configuration",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "generic_feed_configuration",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    job_config_id = table.Column<int>(type: "int", nullable: false),
                    feed_name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    feed_source_type = table.Column<string>(type: "VARCHAR(20)", nullable: false, defaultValue: "REST"),
                    feed_url = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    ftp_host = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    ftp_port = table.Column<int>(type: "int", nullable: true),
                    ftp_path = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    ftp_file_pattern = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    s3_bucket = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    s3_key_prefix = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    local_file_path = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    file_format = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    has_header = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    delimiter = table.Column<string>(type: "VARCHAR(5)", nullable: true),
                    feed_table_name = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    refresh_strategy = table.Column<string>(type: "VARCHAR(20)", nullable: false, defaultValue: "TRUNCATE"),
                    key_column = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    last_download_time = table.Column<DateTime>(type: "datetime2", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    feed_config_json = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_generic_feed_configuration", x => x.id);
                    table.ForeignKey(
                        name: "FK_generic_feed_configuration_generic_job_configuration_job_config_id",
                        column: x => x.job_config_id,
                        principalTable: "generic_job_configuration",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "generic_field_mapping",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    job_config_id = table.Column<int>(type: "int", nullable: false),
                    mapping_type = table.Column<string>(type: "VARCHAR(30)", nullable: false),
                    source_field = table.Column<string>(type: "VARCHAR(200)", nullable: false),
                    target_field = table.Column<string>(type: "VARCHAR(200)", nullable: false),
                    data_type = table.Column<string>(type: "VARCHAR(50)", nullable: false, defaultValue: "VARCHAR"),
                    transform_rule = table.Column<string>(type: "NVARCHAR(500)", nullable: true),
                    is_required = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    sort_order = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_generic_field_mapping", x => x.id);
                    table.ForeignKey(
                        name: "FK_generic_field_mapping_generic_job_configuration_job_config_id",
                        column: x => x.job_config_id,
                        principalTable: "generic_job_configuration",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "generic_queue_routing_rules",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    job_config_id = table.Column<int>(type: "int", nullable: false),
                    result_type = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    queue_id = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_generic_queue_routing_rules", x => x.id);
                    table.ForeignKey(
                        name: "FK_generic_queue_routing_rules_generic_job_configuration_job_config_id",
                        column: x => x.job_config_id,
                        principalTable: "generic_job_configuration",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_auth_config_job_purpose",
                table: "generic_auth_configuration",
                columns: new[] { "job_config_id", "auth_purpose" });

            migrationBuilder.CreateIndex(
                name: "IX_email_config_job_type",
                table: "generic_email_configuration",
                columns: new[] { "job_config_id", "email_type" });

            migrationBuilder.CreateIndex(
                name: "IX_exec_history_job",
                table: "generic_execution_history",
                columns: new[] { "job_config_id", "start_time" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_exec_history_status",
                table: "generic_execution_history",
                columns: new[] { "status", "start_time" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_exec_schedule_job_type",
                table: "generic_execution_schedule",
                columns: new[] { "job_config_id", "schedule_type" });

            migrationBuilder.CreateIndex(
                name: "IX_feed_config_job_id",
                table: "generic_feed_configuration",
                column: "job_config_id");

            migrationBuilder.CreateIndex(
                name: "IX_feed_download_history_job_date",
                table: "generic_feed_download_history",
                columns: new[] { "job_config_id", "download_date" });

            migrationBuilder.CreateIndex(
                name: "IX_field_mapping_job_type_sort",
                table: "generic_field_mapping",
                columns: new[] { "job_config_id", "mapping_type", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_generic_job_config_client_type",
                table: "generic_job_configuration",
                column: "client_type");

            migrationBuilder.CreateIndex(
                name: "IX_generic_job_config_is_active",
                table: "generic_job_configuration",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "IX_generic_job_config_job_id",
                table: "generic_job_configuration",
                column: "job_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_generic_post_history_client",
                table: "generic_post_history",
                column: "client_type");

            migrationBuilder.CreateIndex(
                name: "IX_generic_post_history_item",
                table: "generic_post_history",
                columns: new[] { "item_id", "job_id" });

            migrationBuilder.CreateIndex(
                name: "IX_queue_routing_job_result",
                table: "generic_queue_routing_rules",
                columns: new[] { "job_config_id", "result_type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "generic_auth_configuration");

            migrationBuilder.DropTable(
                name: "generic_email_configuration");

            migrationBuilder.DropTable(
                name: "generic_execution_history");

            migrationBuilder.DropTable(
                name: "generic_execution_schedule");

            migrationBuilder.DropTable(
                name: "generic_feed_configuration");

            migrationBuilder.DropTable(
                name: "generic_feed_download_history");

            migrationBuilder.DropTable(
                name: "generic_field_mapping");

            migrationBuilder.DropTable(
                name: "generic_post_history");

            migrationBuilder.DropTable(
                name: "generic_queue_routing_rules");

            migrationBuilder.DropTable(
                name: "generic_job_configuration");
        }
    }
}
