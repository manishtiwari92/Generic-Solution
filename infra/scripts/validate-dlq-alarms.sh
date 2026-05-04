#!/usr/bin/env bash
# =============================================================================
# validate-dlq-alarms.sh — Validate DLQ alarm behavior in UAT
#
# Usage:
#   ./validate-dlq-alarms.sh
#   ./validate-dlq-alarms.sh --env uat
#   ./validate-dlq-alarms.sh --env uat --skip-cleanup
#
# Steps:
#   1. Send a deliberately malformed JSON message to ips-post-queue-{env}
#   2. Wait for the message to be received 3 times (visibility timeout cycles)
#      and land in ips-post-dlq-{env}
#   3. Poll ips-post-dlq-{env} to confirm the message arrived
#   4. Check that CloudWatch alarm PostDLQAlarm-{env} transitions to ALARM state
#   5. Clean up by purging the DLQ (unless --skip-cleanup is passed)
#
# NOTES:
#   - The visibility timeout on ips-post-queue-{env} must be short enough for
#     this test to complete in a reasonable time. In UAT, set it to 30s for
#     testing, then restore to 7200s after validation.
#   - The maxReceiveCount on the queue must be 3 (default for this platform).
#   - This script requires AWS CLI v2 and jq.
# =============================================================================

set -euo pipefail

# ---------------------------------------------------------------------------
# Defaults
# ---------------------------------------------------------------------------
ENV="uat"
SKIP_CLEANUP=false
AWS_REGION="${AWS_DEFAULT_REGION:-us-east-1}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Timing configuration
# In UAT, temporarily reduce visibility timeout to 30s for this test.
# The script will wait up to 3 * (visibility_timeout + buffer) seconds.
VISIBILITY_TIMEOUT_SECONDS=30   # Must match queue's VisibilityTimeout for this test
POLL_INTERVAL_SECONDS=10
MAX_DLQ_WAIT_SECONDS=300        # 5 minutes max wait for message to reach DLQ
MAX_ALARM_WAIT_SECONDS=600      # 10 minutes max wait for alarm to trigger

# ---------------------------------------------------------------------------
# Argument parsing
# ---------------------------------------------------------------------------
while [[ $# -gt 0 ]]; do
    case "$1" in
        --env)
            ENV="$2"
            shift 2
            ;;
        --skip-cleanup)
            SKIP_CLEANUP=true
            shift
            ;;
        --visibility-timeout)
            VISIBILITY_TIMEOUT_SECONDS="$2"
            shift 2
            ;;
        *)
            echo "Unknown argument: $1" >&2
            echo "Usage: $0 [--env uat|production] [--skip-cleanup] [--visibility-timeout <seconds>]" >&2
            exit 1
            ;;
    esac
done

# ---------------------------------------------------------------------------
# Queue and alarm names
# ---------------------------------------------------------------------------
POST_QUEUE_NAME="ips-post-queue-${ENV}"
POST_DLQ_NAME="ips-post-dlq-${ENV}"
ALARM_NAME="ips-autopost-post-dlq-${ENV}"

log() {
    echo "[$(date -u '+%Y-%m-%dT%H:%M:%SZ')] $*"
}

pass() {
    echo "[PASS] $*"
}

fail() {
    echo "[FAIL] $*" >&2
}

# ---------------------------------------------------------------------------
# Prerequisite checks
# ---------------------------------------------------------------------------
for cmd in aws jq; do
    if ! command -v "${cmd}" &>/dev/null; then
        echo "ERROR: '${cmd}' is required but not installed." >&2
        exit 1
    fi
done

# ---------------------------------------------------------------------------
# Step 1: Get queue URLs
# ---------------------------------------------------------------------------
log "=== Step 1: Resolving queue URLs ==="

POST_QUEUE_URL="$(aws sqs get-queue-url \
    --queue-name "${POST_QUEUE_NAME}" \
    --region "${AWS_REGION}" \
    --query 'QueueUrl' \
    --output text)"

POST_DLQ_URL="$(aws sqs get-queue-url \
    --queue-name "${POST_DLQ_NAME}" \
    --region "${AWS_REGION}" \
    --query 'QueueUrl' \
    --output text)"

log "  Post queue URL : ${POST_QUEUE_URL}"
log "  Post DLQ URL   : ${POST_DLQ_URL}"

# ---------------------------------------------------------------------------
# Step 2: Send a deliberately malformed JSON message
# ---------------------------------------------------------------------------
log "=== Step 2: Sending malformed JSON message to ${POST_QUEUE_NAME} ==="

# This is intentionally malformed — missing required fields, invalid JSON structure
MALFORMED_MESSAGE='{"__dlq_test":true,"invalid_json_structure":{"unclosed_brace":true'

SEND_RESULT="$(aws sqs send-message \
    --queue-url "${POST_QUEUE_URL}" \
    --message-body "${MALFORMED_MESSAGE}" \
    --message-attributes '{"TestMarker":{"DataType":"String","StringValue":"dlq-validation-test"}}' \
    --region "${AWS_REGION}" \
    --output json)"

MESSAGE_ID="$(echo "${SEND_RESULT}" | jq -r '.MessageId')"
log "  Message sent. MessageId: ${MESSAGE_ID}"

# ---------------------------------------------------------------------------
# Step 3: Wait for message to reach DLQ (after 3 receive attempts)
# ---------------------------------------------------------------------------
log "=== Step 3: Waiting for message to reach DLQ (3 receive cycles) ==="
log "  Visibility timeout : ${VISIBILITY_TIMEOUT_SECONDS}s"
log "  Max wait           : ${MAX_DLQ_WAIT_SECONDS}s"
log "  NOTE: The PostWorker must attempt to process this message 3 times."
log "  If the PostWorker is not running, manually receive the message 3 times."

DLQ_MESSAGE_FOUND=false
ELAPSED=0

while [[ ${ELAPSED} -lt ${MAX_DLQ_WAIT_SECONDS} ]]; do
    sleep "${POLL_INTERVAL_SECONDS}"
    ELAPSED=$((ELAPSED + POLL_INTERVAL_SECONDS))

    # Check DLQ for our message
    DLQ_ATTRS="$(aws sqs get-queue-attributes \
        --queue-url "${POST_DLQ_URL}" \
        --attribute-names ApproximateNumberOfMessages \
        --region "${AWS_REGION}" \
        --output json)"

    DLQ_DEPTH="$(echo "${DLQ_ATTRS}" | jq -r '.Attributes.ApproximateNumberOfMessages')"

    log "  [${ELAPSED}s] DLQ depth: ${DLQ_DEPTH}"

    if [[ "${DLQ_DEPTH}" -gt 0 ]]; then
        # Peek at the DLQ to confirm it's our test message
        DLQ_PEEK="$(aws sqs receive-message \
            --queue-url "${POST_DLQ_URL}" \
            --max-number-of-messages 1 \
            --visibility-timeout 30 \
            --region "${AWS_REGION}" \
            --output json 2>/dev/null || echo '{}')"

        DLQ_MSG_BODY="$(echo "${DLQ_PEEK}" | jq -r '.Messages[0].Body // ""')"

        if echo "${DLQ_MSG_BODY}" | grep -q '__dlq_test'; then
            DLQ_MESSAGE_FOUND=true
            DLQ_RECEIPT_HANDLE="$(echo "${DLQ_PEEK}" | jq -r '.Messages[0].ReceiptHandle // ""')"
            log "  Test message confirmed in DLQ."
            break
        else
            log "  DLQ has messages but test message not yet visible (may be other messages)."
        fi
    fi
done

if [[ "${DLQ_MESSAGE_FOUND}" == "true" ]]; then
    pass "Test message arrived in ${POST_DLQ_NAME} after ${ELAPSED}s."
else
    fail "Test message did NOT arrive in ${POST_DLQ_NAME} within ${MAX_DLQ_WAIT_SECONDS}s."
    fail "  Possible causes:"
    fail "    - PostWorker is not running (message not being received)"
    fail "    - Queue visibility timeout is too long (reduce to ${VISIBILITY_TIMEOUT_SECONDS}s for testing)"
    fail "    - maxReceiveCount is not set to 3"
    # Don't exit — still check alarm state and clean up
fi

# ---------------------------------------------------------------------------
# Step 4: Check CloudWatch alarm transitions to ALARM state
# ---------------------------------------------------------------------------
log "=== Step 4: Checking CloudWatch alarm '${ALARM_NAME}' ==="
log "  Max wait: ${MAX_ALARM_WAIT_SECONDS}s"

ALARM_TRIGGERED=false
ELAPSED=0

while [[ ${ELAPSED} -lt ${MAX_ALARM_WAIT_SECONDS} ]]; do
    ALARM_STATE="$(aws cloudwatch describe-alarms \
        --alarm-names "${ALARM_NAME}" \
        --region "${AWS_REGION}" \
        --query 'MetricAlarms[0].StateValue' \
        --output text 2>/dev/null || echo "NOT_FOUND")"

    log "  [${ELAPSED}s] Alarm '${ALARM_NAME}' state: ${ALARM_STATE}"

    if [[ "${ALARM_STATE}" == "ALARM" ]]; then
        ALARM_TRIGGERED=true
        break
    elif [[ "${ALARM_STATE}" == "NOT_FOUND" ]]; then
        fail "Alarm '${ALARM_NAME}' not found. Ensure monitoring.yaml stack is deployed."
        break
    fi

    sleep "${POLL_INTERVAL_SECONDS}"
    ELAPSED=$((ELAPSED + POLL_INTERVAL_SECONDS))
done

if [[ "${ALARM_TRIGGERED}" == "true" ]]; then
    pass "CloudWatch alarm '${ALARM_NAME}' transitioned to ALARM state after ${ELAPSED}s."
else
    fail "CloudWatch alarm '${ALARM_NAME}' did NOT transition to ALARM state within ${MAX_ALARM_WAIT_SECONDS}s."
    fail "  Current state: ${ALARM_STATE}"
    fail "  Check: monitoring.yaml PostDlqAlarm configuration (Period=300, EvaluationPeriods=1, Threshold=1)"
fi

# ---------------------------------------------------------------------------
# Step 5: Cleanup — purge the DLQ
# ---------------------------------------------------------------------------
if [[ "${SKIP_CLEANUP}" == "false" ]]; then
    log "=== Step 5: Cleaning up — purging ${POST_DLQ_NAME} ==="

    aws sqs purge-queue \
        --queue-url "${POST_DLQ_URL}" \
        --region "${AWS_REGION}"

    log "  DLQ purged."
    pass "DLQ ${POST_DLQ_NAME} purged successfully."
else
    log "=== Step 5: Skipping cleanup (--skip-cleanup flag set) ==="
    log "  DLQ ${POST_DLQ_NAME} still contains test messages."
    log "  To clean up manually: aws sqs purge-queue --queue-url ${POST_DLQ_URL}"
fi

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
log ""
log "=== DLQ Alarm Validation Summary ==="
[[ "${DLQ_MESSAGE_FOUND}" == "true" ]] && pass "DLQ message delivery" || fail "DLQ message delivery"
[[ "${ALARM_TRIGGERED}" == "true" ]]   && pass "CloudWatch alarm trigger" || fail "CloudWatch alarm trigger"
[[ "${SKIP_CLEANUP}" == "false" ]]     && pass "DLQ cleanup" || log "[SKIP] DLQ cleanup"
