#!/usr/bin/env bash
# =============================================================================
# deploy-uat.sh — Deploy all three IPS AutoPost CloudFormation stacks to UAT
#
# Usage:
#   ./deploy-uat.sh --env uat
#   ./deploy-uat.sh --env uat --dry-run
#
# Stacks deployed in order:
#   1. infrastructure.yaml  (VPC, SQS, ECR, ECS Cluster, Log Groups)
#   2. application.yaml     (ECS Task Definitions, Services, Scaling)
#   3. monitoring.yaml      (CloudWatch Dashboard, Alarms)
#
# Prerequisites:
#   - AWS CLI v2 configured with credentials that have CloudFormation deploy rights
#   - DEPLOYMENT_ID env var set (or defaults to git short SHA)
# =============================================================================

set -euo pipefail

# ---------------------------------------------------------------------------
# Defaults
# ---------------------------------------------------------------------------
ENV=""
DRY_RUN=false
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
CFN_DIR="${REPO_ROOT}/infra/cloudformation"
AWS_REGION="${AWS_DEFAULT_REGION:-us-east-1}"
DEPLOYMENT_ID="${DEPLOYMENT_ID:-$(git -C "${REPO_ROOT}" rev-parse --short HEAD 2>/dev/null || echo "local")}"

# ---------------------------------------------------------------------------
# Argument parsing
# ---------------------------------------------------------------------------
while [[ $# -gt 0 ]]; do
    case "$1" in
        --env)
            ENV="$2"
            shift 2
            ;;
        --dry-run)
            DRY_RUN=true
            shift
            ;;
        *)
            echo "Unknown argument: $1" >&2
            echo "Usage: $0 --env <uat|production> [--dry-run]" >&2
            exit 1
            ;;
    esac
done

# ---------------------------------------------------------------------------
# Validation
# ---------------------------------------------------------------------------
if [[ -z "${ENV}" ]]; then
    echo "ERROR: --env parameter is required (e.g. --env uat)" >&2
    exit 1
fi

if [[ "${ENV}" != "uat" && "${ENV}" != "production" ]]; then
    echo "ERROR: --env must be 'uat' or 'production', got '${ENV}'" >&2
    exit 1
fi

# ---------------------------------------------------------------------------
# Helper: run or print a command
# ---------------------------------------------------------------------------
run_cmd() {
    if [[ "${DRY_RUN}" == "true" ]]; then
        echo "[DRY-RUN] $*"
    else
        echo "[EXEC] $*"
        "$@"
    fi
}

log() {
    echo "[$(date -u '+%Y-%m-%dT%H:%M:%SZ')] $*"
}

# ---------------------------------------------------------------------------
# Helper: get CloudFormation stack output value
# ---------------------------------------------------------------------------
get_stack_output() {
    local stack_name="$1"
    local output_key="$2"
    aws cloudformation describe-stacks \
        --stack-name "${stack_name}" \
        --region "${AWS_REGION}" \
        --query "Stacks[0].Outputs[?OutputKey=='${output_key}'].OutputValue" \
        --output text
}

# ---------------------------------------------------------------------------
# Stack names
# ---------------------------------------------------------------------------
STACK_INFRA="ips-autopost-infrastructure-${ENV}"
STACK_APP="ips-autopost-application-${ENV}"
STACK_MON="ips-autopost-monitoring-${ENV}"

# ---------------------------------------------------------------------------
# STACK 1: infrastructure.yaml
# ---------------------------------------------------------------------------
log "=== Deploying Stack 1: ${STACK_INFRA} ==="
log "Template: ${CFN_DIR}/infrastructure.yaml"

run_cmd aws cloudformation deploy \
    --stack-name "${STACK_INFRA}" \
    --template-file "${CFN_DIR}/infrastructure.yaml" \
    --parameter-overrides \
        Environment="${ENV}" \
    --capabilities CAPABILITY_IAM CAPABILITY_NAMED_IAM \
    --region "${AWS_REGION}" \
    --no-fail-on-empty-changeset

log "Stack 1 deployed successfully."

# ---------------------------------------------------------------------------
# Retrieve Stack 1 outputs to pass as parameters to Stack 2 and Stack 3
# ---------------------------------------------------------------------------
if [[ "${DRY_RUN}" == "false" ]]; then
    log "Retrieving Stack 1 outputs..."
    PRIVATE_SUBNETS="$(get_stack_output "${STACK_INFRA}" "PrivateSubnets")"
    ECS_SG_ID="$(get_stack_output "${STACK_INFRA}" "ECSSecurityGroupId")"
    ECS_CLUSTER_NAME="$(get_stack_output "${STACK_INFRA}" "ECSClusterName")"
    FEED_QUEUE_URL="$(get_stack_output "${STACK_INFRA}" "FeedQueueURL")"
    POST_QUEUE_URL="$(get_stack_output "${STACK_INFRA}" "PostQueueURL")"
    ECR_REPO_URI="$(get_stack_output "${STACK_INFRA}" "ECRRepositoryURI")"
    FEED_LOG_GROUP="$(get_stack_output "${STACK_INFRA}" "FeedLogGroupName")"
    POST_LOG_GROUP="$(get_stack_output "${STACK_INFRA}" "PostLogGroupName")"

    log "  PrivateSubnets     : ${PRIVATE_SUBNETS}"
    log "  ECSSecurityGroupId : ${ECS_SG_ID}"
    log "  ECSClusterName     : ${ECS_CLUSTER_NAME}"
    log "  FeedQueueURL       : ${FEED_QUEUE_URL}"
    log "  PostQueueURL       : ${POST_QUEUE_URL}"
    log "  ECRRepositoryURI   : ${ECR_REPO_URI}"
else
    # Placeholder values for dry-run display
    PRIVATE_SUBNETS="subnet-xxxxxxxx,subnet-yyyyyyyy"
    ECS_SG_ID="sg-xxxxxxxx"
    ECS_CLUSTER_NAME="ips-autopost-${ENV}"
    FEED_QUEUE_URL="https://sqs.${AWS_REGION}.amazonaws.com/123456789012/ips-feed-queue-${ENV}"
    POST_QUEUE_URL="https://sqs.${AWS_REGION}.amazonaws.com/123456789012/ips-post-queue-${ENV}"
    ECR_REPO_URI="123456789012.dkr.ecr.${AWS_REGION}.amazonaws.com/ecr-ips-autopost-${ENV}"
    FEED_LOG_GROUP="/ips/autopost/feed/${ENV}"
    POST_LOG_GROUP="/ips/autopost/post/${ENV}"
fi

# ---------------------------------------------------------------------------
# STACK 2: application.yaml (depends on Stack 1 outputs)
# ---------------------------------------------------------------------------
log "=== Deploying Stack 2: ${STACK_APP} ==="
log "Template: ${CFN_DIR}/application.yaml"

run_cmd aws cloudformation deploy \
    --stack-name "${STACK_APP}" \
    --template-file "${CFN_DIR}/application.yaml" \
    --parameter-overrides \
        Environment="${ENV}" \
        DeploymentId="${DEPLOYMENT_ID}" \
        PrivateSubnets="${PRIVATE_SUBNETS}" \
        ECSSecurityGroupId="${ECS_SG_ID}" \
        ECSClusterName="${ECS_CLUSTER_NAME}" \
        FeedQueueURL="${FEED_QUEUE_URL}" \
        PostQueueURL="${POST_QUEUE_URL}" \
        ECRRepositoryURI="${ECR_REPO_URI}" \
        FeedLogGroupName="${FEED_LOG_GROUP}" \
        PostLogGroupName="${POST_LOG_GROUP}" \
    --capabilities CAPABILITY_IAM CAPABILITY_NAMED_IAM \
    --region "${AWS_REGION}" \
    --no-fail-on-empty-changeset

log "Stack 2 deployed successfully."

# ---------------------------------------------------------------------------
# Retrieve Stack 2 outputs to pass as parameters to Stack 3
# ---------------------------------------------------------------------------
if [[ "${DRY_RUN}" == "false" ]]; then
    log "Retrieving Stack 2 outputs..."
    FEED_SERVICE_NAME="$(get_stack_output "${STACK_APP}" "FeedWorkerServiceName" 2>/dev/null || echo "")"
    POST_SERVICE_NAME="$(get_stack_output "${STACK_APP}" "PostWorkerServiceName" 2>/dev/null || echo "")"
    log "  FeedWorkerServiceName : ${FEED_SERVICE_NAME}"
    log "  PostWorkerServiceName : ${POST_SERVICE_NAME}"
else
    FEED_SERVICE_NAME="ips-autopost-feed-${ENV}"
    POST_SERVICE_NAME="ips-autopost-post-${ENV}"
fi

# ---------------------------------------------------------------------------
# STACK 3: monitoring.yaml (depends on Stack 1 + Stack 2 outputs)
# ---------------------------------------------------------------------------
log "=== Deploying Stack 3: ${STACK_MON} ==="
log "Template: ${CFN_DIR}/monitoring.yaml"

run_cmd aws cloudformation deploy \
    --stack-name "${STACK_MON}" \
    --template-file "${CFN_DIR}/monitoring.yaml" \
    --parameter-overrides \
        Environment="${ENV}" \
    --capabilities CAPABILITY_IAM CAPABILITY_NAMED_IAM \
    --region "${AWS_REGION}" \
    --no-fail-on-empty-changeset

log "Stack 3 deployed successfully."

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
log "=== All three stacks deployed to ${ENV} ==="
log "  Stack 1 (Infrastructure) : ${STACK_INFRA}"
log "  Stack 2 (Application)    : ${STACK_APP}"
log "  Stack 3 (Monitoring)     : ${STACK_MON}"
log "  Deployment ID            : ${DEPLOYMENT_ID}"
log "  Region                   : ${AWS_REGION}"

if [[ "${DRY_RUN}" == "true" ]]; then
    log "DRY-RUN complete — no changes were made."
fi
