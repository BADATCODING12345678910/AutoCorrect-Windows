# Creates a PayPal subscription approve URL for a plan.
# Usage:
#   .\CreateSubscribeLink.ps1 personal
#   .\CreateSubscribeLink.ps1 pro
#   .\CreateSubscribeLink.ps1 team
param(
  [Parameter(Mandatory = $true)]
  [ValidateSet("personal", "pro", "team")]
  [string]$Tier
)

$ErrorActionPreference = "Stop"
$credPath = Join-Path $env:LOCALAPPDATA "AutoCorrect\PayPalCredentials.ini"
$plansPath = Join-Path $env:LOCALAPPDATA "AutoCorrect\PayPalPlans.json"
if (-not (Test-Path $credPath)) { throw "Missing $credPath" }
if (-not (Test-Path $plansPath)) { throw "Missing $plansPath — recreate plans first" }

$ini = Get-Content $credPath -Raw
$cid = ([regex]::Match($ini, '(?m)^ClientId=(.+)$')).Groups[1].Value.Trim()
$sec = ([regex]::Match($ini, '(?m)^ClientSecret=(.+)$')).Groups[1].Value.Trim()
$pair = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${cid}:${sec}"))
$token = (Invoke-RestMethod -Method Post -Uri "https://api-m.paypal.com/v1/oauth2/token" `
  -Headers @{ Authorization = "Basic $pair" } `
  -ContentType "application/x-www-form-urlencoded" `
  -Body "grant_type=client_credentials").access_token

$plans = Get-Content $plansPath | ConvertFrom-Json
$key = switch ($Tier) {
  "personal" { "personal_yearly" }
  "pro" { "pro_yearly" }
  "team" { "team_monthly" }
}
$plan = $plans.plans.$key
$body = @{
  plan_id = $plan.id
  custom_id = $plan.custom_id
  application_context = @{
    brand_name = "AutoCorrect"
    user_action = "SUBSCRIBE_NOW"
    return_url = "https://arsfrcafaoagrjbnjydx.supabase.co/functions/v1/license-api"
    cancel_url = "https://www.paypal.com/ncp/payment/PA3R3XQRAF8QG"
  }
} | ConvertTo-Json -Depth 6

$sub = Invoke-RestMethod -Method Post -Uri "https://api-m.paypal.com/v1/billing/subscriptions" `
  -Headers @{
    Authorization = "Bearer $token"
    "Content-Type" = "application/json"
    "PayPal-Request-Id" = [guid]::NewGuid().ToString()
  } -Body $body

$approve = ($sub.links | Where-Object { $_.rel -eq "approve" }).href
Write-Output "subscription_id=$($sub.id)"
Write-Output "status=$($sub.status)"
Write-Output "approve_url=$approve"
if ($approve) { Start-Process $approve }
