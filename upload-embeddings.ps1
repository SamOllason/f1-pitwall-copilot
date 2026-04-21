$envPath = "src/Web/.env"
$lines = Get-Content $envPath

function Get-EnvValue($key, $lines) {
  $line = $lines | Where-Object { $_ -match "^$key=" } | Select-Object -First 1
  if (-not $line) { return $null }
  return $line.Substring($key.Length + 1).Trim()
}

$searchEndpoint = Get-EnvValue "AZURE_SEARCH_ENDPOINT" $lines
$searchApiKey = Get-EnvValue "AZURE_SEARCH_API_KEY" $lines
$indexName = Get-EnvValue "AZURE_SEARCH_INDEX_NAME" $lines

$inputFile = "sample-data/rag/chunks.with-vectors.ndjson"
$apiVersion = "2024-07-01"

$docs = Get-Content $inputFile | ForEach-Object {
  if (-not [string]::IsNullOrWhiteSpace($_)) {
    $d = $_ | ConvertFrom-Json
    $d | Add-Member -NotePropertyName "@search.action" -NotePropertyValue "upload"
    $d
  }
}

$body = @{ value = $docs } | ConvertTo-Json -Depth 100
$url = "$searchEndpoint/indexes/$indexName/docs/index?api-version=$apiVersion"

$headers = @{
  "Content-Type" = "application/json"
  "api-key" = $searchApiKey
}

$response = Invoke-RestMethod -Method Post -Uri $url -Headers $headers -Body $body
$response