#!/usr/bin/env bash
# =============================================================================
# run-migrations-uat.sh — Run EF Core migrations and seed scripts on UAT DB
#
# Usage:
#   UAT_CONNECTION_STRING="Server=...;Database=Workflow;..." ./run-migrations-uat.sh
#
# Steps:
#   1. Run EF Core database update (creates all 10 generic tables)
#   2. Run seed scripts in order:
#      8.1 — InvitedClub job configuration
#      8.2 — InvitedClub execution schedule
#      8.3 — Sevita job configuration
#      8.4 — Sevita execution schedule
#   3. Verify all 10 generic tables exist
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
SEED_DIR="${REPO_ROOT}/db/seed"
CORE_PROJECT="${REPO_ROOT}/src/IPS.AutoPost.Core"

# ---------------------------------------------------------------------------
# Validate prerequisites
# ---------------------------------------------------------------------------
if [[ -z "${UAT_CONNECTION_STRING:-}" ]]; then
    echo "ERROR: UAT_CONNECTION_STRING environment variable is not set." >&2
    echo "  Example: export UAT_CONNECTION_STRING=\"Server=ips-rds-database-1.cmrmduasa2gk.us-east-1.rds.amazonaws.com;Database=Workflow;User ID=IPSAppsUser;Password=...;Max Pool Size=2000;\"" >&2
    exit 1
fi

log() {
    echo "[$(date -u '+%Y-%m-%dT%H:%M:%SZ')] $*"
}

# ---------------------------------------------------------------------------
# Helper: run a SQL script via sqlcmd
# Requires sqlcmd (mssql-tools) to be installed.
# ---------------------------------------------------------------------------
run_sql_script() {
    local script_path="$1"
    local description="$2"

    log "Running SQL script: ${description}"
    log "  File: ${script_path}"

    if ! command -v sqlcmd &>/dev/null; then
        echo "ERROR: sqlcmd not found. Install mssql-tools: https://docs.microsoft.com/en-us/sql/linux/sql-server-linux-setup-tools" >&2
        exit 1
    fi

    # Parse connection string components for sqlcmd
    # Expects format: Server=host;Database=db;User ID=user;Password=pass;...
    local server database user password
    server="$(echo "${UAT_CONNECTION_STRING}" | grep -oP '(?<=Server=)[^;]+')"
    database="$(echo "${UAT_CONNECTION_STRING}" | grep -oP '(?<=Database=)[^;]+')"
    user="$(echo "${UAT_CONNECTION_STRING}" | grep -oP '(?<=User ID=)[^;]+')"
    password="$(echo "${UAT_CONNECTION_STRING}" | grep -oP '(?<=Password=)[^;]+')"

    sqlcmd \
        -S "${server}" \
        -d "${database}" \
        -U "${user}" \
        -P "${password}" \
        -i "${script_path}" \
        -b \
        -v SQLCMDMAXVARTYPEWIDTH=8000

    log "  [OK] ${description} completed."
}

# ---------------------------------------------------------------------------
# STEP 1: EF Core database update
# ---------------------------------------------------------------------------
log "=== STEP 1: EF Core database update ==="
log "  Project : ${CORE_PROJECT}"
log "  Target  : UAT Workflow DB"

if ! command -v dotnet &>/dev/null; then
    echo "ERROR: dotnet CLI not found. Install .NET 10 SDK." >&2
    exit 1
fi

dotnet ef database update \
    --project "${CORE_PROJECT}" \
    --connection "${UAT_CONNECTION_STRING}" \
    --verbose

log "[OK] EF Core migrations applied."

# ---------------------------------------------------------------------------
# STEP 2: Seed scripts in order
# ---------------------------------------------------------------------------
log "=== STEP 2: Running seed scripts ==="

run_sql_script \
    "${SEED_DIR}/01_seed_invitedclub_job_configuration.sql" \
    "8.1 — InvitedClub job configuration"

run_sql_script \
    "${SEED_DIR}/02_seed_invitedclub_execution_schedule.sql" \
    "8.2 — InvitedClub execution schedule"

run_sql_script \
    "${SEED_DIR}/03_seed_sevita_job_configuration.sql" \
    "8.3 — Sevita job configuration"

run_sql_script \
    "${SEED_DIR}/04_seed_sevita_execution_schedule.sql" \
    "8.4 — Sevita execution schedule"

log "[OK] All seed scripts completed."

# ---------------------------------------------------------------------------
# STEP 3: Verify all 10 generic tables exist
# ---------------------------------------------------------------------------
log "=== STEP 3: Verifying all 10 generic tables exist ==="

VERIFY_SQL=$(cat <<'EOF'
SET NOCOUNT ON;

DECLARE @expected TABLE (table_name VARCHAR(100));
INSERT INTO @expected VALUES
    ('generic_job_configuration'),
    ('generic_execution_schedule'),
    ('generic_feed_configuration'),
    ('generic_auth_configuration'),
    ('generic_queue_routing_rules'),
    ('generic_post_history'),
    ('generic_email_configuration'),
    ('generic_feed_download_history'),
    ('generic_execution_history'),
    ('generic_field_mapping');

DECLARE @missing TABLE (table_name VARCHAR(100));
INSERT INTO @missing
SELECT e.table_name
FROM @expected e
WHERE NOT EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.TABLES t
    WHERE t.TABLE_NAME = e.table_name
      AND t.TABLE_SCHEMA = 'dbo'
);

DECLARE @missing_count INT = (SELECT COUNT(*) FROM @missing);
DECLARE @found_count   INT = (SELECT COUNT(*) FROM @expected) - @missing_count;

PRINT CONCAT('Tables found   : ', @found_count, ' / 10');
PRINT CONCAT('Tables missing : ', @missing_count);

IF @missing_count > 0
BEGIN
    PRINT 'MISSING TABLES:';
    SELECT table_name AS MissingTable FROM @missing;
    RAISERROR('Verification FAILED: %d generic table(s) are missing.', 16, 1, @missing_count);
END
ELSE
BEGIN
    PRINT 'Verification PASSED: All 10 generic tables exist.';
END
EOF
)

# Write to temp file and run
TEMP_SQL=$(mktemp /tmp/verify_tables_XXXXXX.sql)
echo "${VERIFY_SQL}" > "${TEMP_SQL}"

run_sql_script "${TEMP_SQL}" "Table existence verification"
rm -f "${TEMP_SQL}"

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
log "=== Migration and seed completed successfully ==="
log "  EF Core migrations : applied"
log "  Seed scripts       : 4 scripts executed"
log "  Table verification : 10/10 tables confirmed"
