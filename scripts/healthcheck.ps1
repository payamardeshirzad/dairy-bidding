Write-Host "=== Dairy Bidding Local Infra Health Check ===" -ForegroundColor Cyan

function Check-Url($name, $url) {
  try {
    $r = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 10
    if ($r.StatusCode -ge 200 -and $r.StatusCode -lt 400) {
      Write-Host "[PASS] $name -> $url" -ForegroundColor Green
    } else {
      Write-Host "[FAIL] $name -> $url (Status: $($r.StatusCode))" -ForegroundColor Red
    }
  } catch {
    Write-Host "[FAIL] $name -> $url ($($_.Exception.Message))" -ForegroundColor Red
  }
}

# Containers
$containers = @(
  "dairy-postgres",
  "dairy-redis",
  "dairy-rabbitmq",
  "dairy-minio",
  "dairy-mailpit",
  "dairy-prometheus",
  "dairy-grafana",
  "dairy-jaeger",
  "dairy-elasticsearch"
)

Write-Host "`n-- Container status --" -ForegroundColor Yellow
foreach ($c in $containers) {
  $status = podman inspect -f "{{.State.Status}}" $c 2>$null
  if ($status -eq "running") {
    Write-Host "[PASS] $c is running" -ForegroundColor Green
  } else {
    Write-Host "[FAIL] $c is not running" -ForegroundColor Red
  }
}

Write-Host "`n-- Endpoint checks --" -ForegroundColor Yellow
Check-Url "RabbitMQ UI" "http://localhost:15672"
Check-Url "MinIO Console" "http://localhost:9001"
Check-Url "Mailpit UI" "http://localhost:8025"
Check-Url "Prometheus" "http://localhost:9090/-/healthy"
Check-Url "Grafana API" "http://localhost:3000/api/health"
Check-Url "Jaeger UI" "http://localhost:16686"
Check-Url "Elasticsearch" "http://localhost:9200"

Write-Host "`n-- .NET Service health --" -ForegroundColor Yellow
Check-Url "ApiGateway" "http://localhost:5000/health"
Check-Url "IdentityService" "http://localhost:5245/health"
Check-Url "AuctionService" "http://localhost:5255/health"
Check-Url "BiddingService" "http://localhost:5170/health"

Write-Host "`n-- Deep checks --" -ForegroundColor Yellow
# Redis
$redisPing = podman exec dairy-redis redis-cli ping 2>$null
if ($redisPing -match "PONG") { Write-Host "[PASS] Redis ping" -ForegroundColor Green } else { Write-Host "[FAIL] Redis ping" -ForegroundColor Red }

# Postgres
try {
  podman exec dairy-postgres psql -U dairy_admin -d postgres -c "\l" | Out-Null
  Write-Host "[PASS] PostgreSQL connection/list DBs" -ForegroundColor Green
} catch {
  Write-Host "[FAIL] PostgreSQL connection" -ForegroundColor Red
}

# RabbitMQ API auth
try {
  $pair = "dairy:dairy_local_pass"
  $bytes = [System.Text.Encoding]::ASCII.GetBytes($pair)
  $b64 = [Convert]::ToBase64String($bytes)
  $headers = @{ Authorization = "Basic $b64" }
  $r = Invoke-RestMethod -Uri "http://localhost:15672/api/overview" -Headers $headers -TimeoutSec 10
  if ($r.rabbitmq_version) {
    Write-Host "[PASS] RabbitMQ management API auth" -ForegroundColor Green
  } else {
    Write-Host "[FAIL] RabbitMQ API response invalid" -ForegroundColor Red
  }
} catch {
  Write-Host "[FAIL] RabbitMQ management API auth" -ForegroundColor Red
}

Write-Host "`nHealth check completed." -ForegroundColor Cyan