using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HuntOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DomainModelAndApiKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "api_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    prefix = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    scopes = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    last_used_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_keys", x => x.id);
                    table.CheckConstraint("ck_api_keys_hash_sha256", "octet_length(hash) = 32");
                    table.CheckConstraint("ck_api_keys_prefix_format", "prefix ~ '^hops_[a-z0-9]{8}$'");
                    table.CheckConstraint("ck_api_keys_scopes", "scopes BETWEEN 1 AND 3");
                });

            migrationBuilder.CreateTable(
                name: "event_types",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_window_by_default = table.Column<bool>(type: "boolean", nullable: false),
                    default_action_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    archived_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_types", x => x.key);
                    table.CheckConstraint("ck_event_types_category", "category IN ('Application', 'Purchase', 'Results', 'Reporting', 'Season', 'Other')");
                    table.CheckConstraint("ck_event_types_key_format", "key ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
                });

            migrationBuilder.CreateTable(
                name: "jurisdictions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    website_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    archived_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jurisdictions", x => x.id);
                    table.CheckConstraint("ck_jurisdictions_code_upper", "code = upper(code) AND length(code) BETWEEN 1 AND 10");
                    table.CheckConstraint("ck_jurisdictions_country", "country ~ '^[A-Z]{2}$'");
                    table.CheckConstraint("ck_jurisdictions_name_not_blank", "length(btrim(name)) > 0");
                });

            migrationBuilder.CreateTable(
                name: "agencies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    jurisdiction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    abbreviation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    website_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    archived_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agencies", x => x.id);
                    table.CheckConstraint("ck_agencies_name_not_blank", "length(btrim(name)) > 0");
                    table.ForeignKey(
                        name: "fk_agencies_jurisdictions_jurisdiction_id",
                        column: x => x.jurisdiction_id,
                        principalTable: "jurisdictions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "programs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_program_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    species = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    method = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    residency = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    permit_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    unit = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    attributes = table.Column<string>(type: "jsonb", nullable: false),
                    website_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    archived_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_programs", x => x.id);
                    table.CheckConstraint("ck_programs_name_not_blank", "length(btrim(name)) > 0");
                    table.CheckConstraint("ck_programs_not_own_parent", "parent_program_id IS NULL OR parent_program_id <> id");
                    table.CheckConstraint("ck_programs_slug_format", "slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
                    table.ForeignKey(
                        name: "fk_programs_agencies_agency_id",
                        column: x => x.agency_id,
                        principalTable: "agencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_programs_programs_parent_program_id",
                        column: x => x.parent_program_id,
                        principalTable: "programs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "program_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    program_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    qualifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: ""),
                    season_year = table.Column<int>(type: "integer", nullable: false),
                    season_label = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    start_date = table.Column<LocalDate>(type: "date", nullable: false),
                    start_time = table.Column<LocalTime>(type: "time", nullable: true),
                    end_date = table.Column<LocalDate>(type: "date", nullable: true),
                    end_time = table.Column<LocalTime>(type: "time", nullable: true),
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    starts_at_utc = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    ends_at_utc = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    source_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    origin_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    external_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    verification_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    last_verified_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    verified_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    last_source_confirmed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    archived_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_program_events", x => x.id);
                    table.CheckConstraint("ck_program_events_end_after_start", "ends_at_utc IS NULL OR ends_at_utc >= starts_at_utc");
                    table.CheckConstraint("ck_program_events_end_after_start_date", "end_date IS NULL OR end_date >= start_date");
                    table.CheckConstraint("ck_program_events_end_time_needs_end_date", "end_time IS NULL OR end_date IS NOT NULL");
                    table.CheckConstraint("ck_program_events_name_not_blank", "length(btrim(name)) > 0");
                    table.CheckConstraint("ck_program_events_origin_kind", "origin_kind IN ('Manual', 'Import', 'Source', 'Mcp')");
                    table.CheckConstraint("ck_program_events_season_year", "season_year BETWEEN 1900 AND 2200");
                    table.CheckConstraint("ck_program_events_verification_status", "verification_status IN ('Unverified', 'Verified')");
                    table.CheckConstraint("ck_program_events_window_instants", "(end_date IS NULL) = (ends_at_utc IS NULL)");
                    table.ForeignKey(
                        name: "fk_program_events_event_types_event_type_key",
                        column: x => x.event_type_key,
                        principalTable: "event_types",
                        principalColumn: "key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_program_events_programs_program_id",
                        column: x => x.program_id,
                        principalTable: "programs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "required_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    program_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_optional = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    archived_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_required_actions", x => x.id);
                    table.CheckConstraint("ck_required_actions_title_not_blank", "length(btrim(title)) > 0");
                    table.ForeignKey(
                        name: "fk_required_actions_program_events_program_event_id",
                        column: x => x.program_event_id,
                        principalTable: "program_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "action_status_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    required_action_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    outcome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    changed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    changed_via = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    changed_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_action_status_changes", x => x.id);
                    table.CheckConstraint("ck_action_status_changes_changed_via", "changed_via IN ('Web', 'Api', 'Mcp', 'Notification', 'Cli', 'System')");
                    table.CheckConstraint("ck_action_status_changes_status", "status IN ('Completed', 'NotApplicable', 'Cancelled', 'Reopened')");
                    table.ForeignKey(
                        name: "fk_action_status_changes_required_actions_required_action_id",
                        column: x => x.required_action_id,
                        principalTable: "required_actions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Action status history is append-only (architecture R4): reject UPDATE, DELETE and TRUNCATE at the
            // database level so no code path, tool or manual query can rewrite a user's decisions.
            migrationBuilder.Sql("""
                CREATE FUNCTION huntops_reject_history_modification() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'table % is append-only; % is not allowed', TG_TABLE_NAME, TG_OP
                        USING ERRCODE = 'restrict_violation';
                END
                $$;

                CREATE TRIGGER action_status_changes_append_only
                    BEFORE UPDATE OR DELETE ON action_status_changes
                    FOR EACH ROW EXECUTE FUNCTION huntops_reject_history_modification();

                CREATE TRIGGER action_status_changes_no_truncate
                    BEFORE TRUNCATE ON action_status_changes
                    FOR EACH STATEMENT EXECUTE FUNCTION huntops_reject_history_modification();
                """);

            migrationBuilder.InsertData(
                table: "event_types",
                columns: new[] { "key", "archived_at", "category", "created_at", "default_action_title", "description", "display_name", "is_system", "is_window_by_default", "updated_at" },
                values: new object[,]
                {
                    { "application-period", null, "Application", NodaTime.Instant.FromUnixTimeTicks(17910720000000000L), "Submit application", "Window during which draw or permit applications are accepted.", "Application period", true, true, NodaTime.Instant.FromUnixTimeTicks(17910720000000000L) },
                    { "application-withdrawal", null, "Application", NodaTime.Instant.FromUnixTimeTicks(17910720000000000L), null, "Last day to withdraw an application, surrender a tag or request a refund.", "Application withdrawal or refund deadline", true, false, NodaTime.Instant.FromUnixTimeTicks(17910720000000000L) },
                    { "draw-results", null, "Results", NodaTime.Instant.FromUnixTimeTicks(17910720000000000L), null, "Date draw results are expected to be published.", "Draw results", true, false, NodaTime.Instant.FromUnixTimeTicks(17910720000000000L) },
                    { "harvest-reporting", null, "Reporting", NodaTime.Instant.FromUnixTimeTicks(17910720000000000L), "Submit harvest report", "Period during which mandatory harvest or survey reports must be submitted.", "Harvest reporting", true, true, NodaTime.Instant.FromUnixTimeTicks(17910720000000000L) },
                    { "leftover-sale", null, "Purchase", NodaTime.Instant.FromUnixTimeTicks(17910720000000000L), "Purchase leftover tag", "Sale or draw of leftover, returned or reissued tags.", "Leftover or reissue sale", true, false, NodaTime.Instant.FromUnixTimeTicks(17910720000000000L) },
                    { "license-purchase", null, "Purchase", NodaTime.Instant.FromUnixTimeTicks(17910720000000000L), "Purchase license or tag", "Window for purchasing a license, tag or permit that was drawn or is otherwise available.", "License or tag purchase", true, true, NodaTime.Instant.FromUnixTimeTicks(17910720000000000L) },
                    { "otc-sale", null, "Purchase", NodaTime.Instant.FromUnixTimeTicks(17910720000000000L), "Purchase OTC tag", "Date over-the-counter licenses or tags go on sale.", "Over-the-counter sale", true, false, NodaTime.Instant.FromUnixTimeTicks(17910720000000000L) },
                    { "other", null, "Other", NodaTime.Instant.FromUnixTimeTicks(17910720000000000L), null, "Any other dated item worth tracking.", "Other", true, false, NodaTime.Instant.FromUnixTimeTicks(17910720000000000L) },
                    { "preference-point-period", null, "Purchase", NodaTime.Instant.FromUnixTimeTicks(17910720000000000L), "Purchase preference point", "Window for buying a preference or bonus point without applying for a tag.", "Preference/bonus point purchase", true, true, NodaTime.Instant.FromUnixTimeTicks(17910720000000000L) },
                    { "season", null, "Season", NodaTime.Instant.FromUnixTimeTicks(17910720000000000L), null, "Hunting season dates.", "Season", true, true, NodaTime.Instant.FromUnixTimeTicks(17910720000000000L) }
                });

            migrationBuilder.CreateIndex(
                name: "ix_action_status_changes_required_action_id_user_id_changed_at",
                table: "action_status_changes",
                columns: new[] { "required_action_id", "user_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_action_status_changes_sequence",
                table: "action_status_changes",
                column: "sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_agencies_jurisdiction_name_active",
                table: "agencies",
                columns: new[] { "jurisdiction_id", "name_key" },
                unique: true,
                filter: "archived_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_prefix",
                table: "api_keys",
                column: "prefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_jurisdictions_code_active",
                table: "jurisdictions",
                column: "code",
                unique: true,
                filter: "archived_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_jurisdictions_name_active",
                table: "jurisdictions",
                column: "name_key",
                unique: true,
                filter: "archived_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_program_events_ends_at_utc",
                table: "program_events",
                column: "ends_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_program_events_event_type_key",
                table: "program_events",
                column: "event_type_key");

            migrationBuilder.CreateIndex(
                name: "ix_program_events_starts_at_utc",
                table: "program_events",
                column: "starts_at_utc");

            migrationBuilder.CreateIndex(
                name: "ux_program_events_natural_key_active",
                table: "program_events",
                columns: new[] { "program_id", "season_year", "event_type_key", "qualifier" },
                unique: true,
                filter: "archived_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_programs_parent_program_id",
                table: "programs",
                column: "parent_program_id");

            migrationBuilder.CreateIndex(
                name: "ix_programs_species",
                table: "programs",
                column: "species");

            migrationBuilder.CreateIndex(
                name: "ux_programs_agency_name_active",
                table: "programs",
                columns: new[] { "agency_id", "name_key" },
                unique: true,
                filter: "archived_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_programs_slug_active",
                table: "programs",
                column: "slug",
                unique: true,
                filter: "archived_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_required_actions_program_event_id",
                table: "required_actions",
                column: "program_event_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS action_status_changes_no_truncate ON action_status_changes;
                DROP TRIGGER IF EXISTS action_status_changes_append_only ON action_status_changes;
                DROP FUNCTION IF EXISTS huntops_reject_history_modification();
                """);

            migrationBuilder.DropTable(
                name: "action_status_changes");

            migrationBuilder.DropTable(
                name: "api_keys");

            migrationBuilder.DropTable(
                name: "required_actions");

            migrationBuilder.DropTable(
                name: "program_events");

            migrationBuilder.DropTable(
                name: "event_types");

            migrationBuilder.DropTable(
                name: "programs");

            migrationBuilder.DropTable(
                name: "agencies");

            migrationBuilder.DropTable(
                name: "jurisdictions");
        }
    }
}
