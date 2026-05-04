# =============================================================================
# validate-cloudwatch-metrics.ps1
# UAT Validation — CloudWatch Metrics Check
# =============================================================================
#
# PURPOSE:
#   Verify that PostSuccessCount and PostFailedCount metrics exist in the
#   IPS/AutoPost/uat CloudWatch namespace and have data points in the last hour.
#   Also checks that ClientType and JobId dimensions are present.
#
# USAGE:
#   .\validate-cloudwatch-metrics.ps1
#   .\validate-cloudwatch-metrics.ps1 -Environment production
#   .\validate-cloudwatch-metrics.ps1 -LookbackMinutes 120
#
# PREREQUISITES:
#   - AWS CLI v2 installed and configured
#   - PowerShell 7+ (or Windows PowerShell 5.1)
# =============================================================================

param(
    [string]$Environment    = "uat",
    [int]$LookbackMinutes   = 60,
    [string]$AwsRegion      = $env:AWS_DEFAULT_REGION ?? "us-east-1"
)

$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------
$Namespace      = "IPS/AutoPost/$Environment"
$StartTime      = (Get-Date).ToUniversalTime().AddMinutes(-$LookbackMinutes).ToString("yyyy-MM-ddTHH:mm:ssZ")
$EndTime        = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$RequiredMetrics = @("PostSuccessCount", "PostFailedCount")
$RequiredDimensions = @("ClientType", "JobId")

$PassCount = 0
$FailCount = 0

function Write-Pass {
    param([string]$Message)
    Write-Host "[PASS] $Message" -ForegroundColor Green
    $script:PassCount++
}

function Write-Fail {
    param([string]$Message)
    Write-Host "[FAIL] $Message" -ForegroundColor Red
    $script:FailCount++
}

function Write-Info {
    param([string]$Message)
    Write-Host "[INFO] $Message" -ForegroundColor Cyan
}

Write-Host ""
Write-Host "=== CloudWatch Metrics Validation ===" -ForegroundColor Yellow
Write-Host "Namespace  : $Namespace"
Write-Host "Region     : $AwsRegion"
Write-Host "Window     : Last $LookbackMinutes minutes ($StartTime -> $EndTime)"
Write-Host ""

# ---------------------------------------------------------------------------
# Step 1: List all metrics in the namespace
# ---------------------------------------------------------------------------
Write-Info "Listing metrics in namespace '$Namespace'..."

try {
    $listMetricsJson = aws cloudwatch list-metrics `
        --namespace $Namespace `
        --region $AwsRegion `
        --output json 2>&1

    if ($LASTEXITCODE -ne 0) {
        Write-Fail "aws cloudwatch list-metrics failed: $listMetricsJson"
        exit 1
    }

    $metricsResponse = $listMetricsJson | ConvertFrom-Json
    $allMetrics = $metricsResponse.Metrics
}
catch {
    Write-Fail "Failed to list CloudWatch metrics: $_"
    exit 1
}

Write-Info "Found $($allMetrics.Count) metric(s) in namespace '$Namespace'."
Write-Host ""

# ---------------------------------------------------------------------------
# Step 2: Check each required metric exists and has data points
# ---------------------------------------------------------------------------
foreach ($metricName in $RequiredMetrics) {
    Write-Host "--- Checking metric: $metricName ---"

    # Check metric exists in the namespace
    $matchingMetrics = $allMetrics | Where-Object { $_.MetricName -eq $metricName }

    if ($matchingMetrics.Count -eq 0) {
        Write-Fail "Metric '$metricName' does NOT exist in namespace '$Namespace'."
        Write-Host ""
        continue
    }

    Write-Pass "Metric '$metricName' exists in namespace '$Namespace' ($($matchingMetrics.Count) dimension combination(s))."

    # ---------------------------------------------------------------------------
    # Step 3: Check that ClientType and JobId dimensions are present
    # ---------------------------------------------------------------------------
    $hasDimensions = $false
    foreach ($metric in $matchingMetrics) {
        $dimensionNames = $metric.Dimensions | ForEach-Object { $_.Name }
        $missingDims = $RequiredDimensions | Where-Object { $_ -notin $dimensionNames }

        if ($missingDims.Count -eq 0) {
            $hasDimensions = $true
            $dimDisplay = ($metric.Dimensions | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ", "
            Write-Pass "Metric '$metricName' has required dimensions: $dimDisplay"
            break
        }
    }

    if (-not $hasDimensions) {
        $foundDims = ($matchingMetrics[0].Dimensions | ForEach-Object { $_.Name }) -join ", "
        Write-Fail "Metric '$metricName' is missing required dimensions. Found: [$foundDims]. Required: [ClientType, JobId]."
    }

    # ---------------------------------------------------------------------------
    # Step 4: Check that the metric has data points in the lookback window
    # ---------------------------------------------------------------------------
    # Use the first metric entry that has both required dimensions
    $targetMetric = $matchingMetrics | Where-Object {
        $dimNames = $_.Dimensions | ForEach-Object { $_.Name }
        ($RequiredDimensions | Where-Object { $_ -notin $dimNames }).Count -eq 0
    } | Select-Object -First 1

    if ($null -eq $targetMetric) {
        $targetMetric = $matchingMetrics | Select-Object -First 1
    }

    # Build dimension filter for get-metric-statistics
    $dimensionArgs = @()
    foreach ($dim in $targetMetric.Dimensions) {
        $dimensionArgs += "Name=$($dim.Name),Value=$($dim.Value)"
    }

    try {
        $statsJson = aws cloudwatch get-metric-statistics `
            --namespace $Namespace `
            --metric-name $metricName `
            --dimensions $dimensionArgs `
            --start-time $StartTime `
            --end-time $EndTime `
            --period 3600 `
            --statistics Sum `
            --region $AwsRegion `
            --output json 2>&1

        if ($LASTEXITCODE -ne 0) {
            Write-Fail "get-metric-statistics failed for '$metricName': $statsJson"
            Write-Host ""
            continue
        }

        $statsResponse = $statsJson | ConvertFrom-Json
        $dataPoints = $statsResponse.Datapoints

        if ($dataPoints.Count -gt 0) {
            $totalSum = ($dataPoints | Measure-Object -Property Sum -Sum).Sum
            Write-Pass "Metric '$metricName' has $($dataPoints.Count) data point(s) in the last $LookbackMinutes minutes. Total Sum = $totalSum."
        }
        else {
            Write-Fail "Metric '$metricName' has NO data points in the last $LookbackMinutes minutes. The platform may not have processed any jobs recently."
        }
    }
    catch {
        Write-Fail "Exception querying data points for '$metricName': $_"
    }

    Write-Host ""
}

# ---------------------------------------------------------------------------
# Step 5: Additional metrics check (FeedSuccessCount, FeedFailedCount)
# ---------------------------------------------------------------------------
Write-Host "--- Checking additional feed metrics ---"
$feedMetrics = @("FeedSuccessCount", "FeedFailedCount", "PostDurationSeconds", "FeedDurationSeconds")

foreach ($metricName in $feedMetrics) {
    $exists = $allMetrics | Where-Object { $_.MetricName -eq $metricName }
    if ($exists.Count -gt 0) {
        Write-Pass "Feed metric '$metricName' exists ($($exists.Count) dimension combination(s))."
    }
    else {
        Write-Info "Feed metric '$metricName' not found (may be expected if no feed jobs have run)."
    }
}

Write-Host ""

# ---------------------------------------------------------------------------
# Final summary
# ---------------------------------------------------------------------------
Write-Host "=== Validation Summary ===" -ForegroundColor Yellow
Write-Host "PASS : $PassCount" -ForegroundColor Green
Write-Host "FAIL : $FailCount" -ForegroundColor $(if ($FailCount -gt 0) { "Red" } else { "Green" })
Write-Host ""

if ($FailCount -gt 0) {
    Write-Host "RESULT: FAIL — $FailCount check(s) failed." -ForegroundColor Red
    exit 1
}
else {
    Write-Host "RESULT: PASS — All CloudWatch metric checks passed." -ForegroundColor Green
    exit 0
}
