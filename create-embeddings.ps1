$envPath = "src/Web/.env"
$lines = Get-Content $envPath

function Get-EnvValue($key, $lines) {
  $line = $lines | Where-Object { $_ -match "^$key=" } | Select-Object -First 1
  if (-not $line) { return $null }
  return $line.Substring($key.Length + 1).Trim()
}

$endpoint = Get-EnvValue "OPENAI_ENDPOINT" $lines
$apiKey = Get-EnvValue "OPENAI_API_KEY" $lines
$embeddingModel = Get-EnvValue "OPENAI_EMBEDDING_MODEL" $lines

if (-not $endpoint -or -not $apiKey -or -not $embeddingModel) {
  throw "Missing OPENAI_ENDPOINT, OPENAI_API_KEY, or OPENAI_EMBEDDING_MODEL in src/Web/.env"
}

$apiVersion = "2024-02-15-preview"
$inputFile = "sample-data/rag/chunks.ndjson"
$outputFile = "sample-data/rag/chunks.with-vectors.ndjson"

if (Test-Path $outputFile) { Remove-Item $outputFile }

$headers = @{
  "api-key" = $apiKey
  "Content-Type" = "application/json"
}

Get-Content $inputFile | ForEach-Object {
  if ([string]::IsNullOrWhiteSpace($_)) { return }

  $doc = $_ | ConvertFrom-Json
  $body = @{
    input = $doc.content
  } | ConvertTo-Json -Depth 5

  $url = "$endpoint/openai/deployments/$embeddingModel/embeddings?api-version=$apiVersion"
  $resp = Invoke-RestMethod -Method Post -Uri $url -Headers $headers -Body $body

  $doc | Add-Member -NotePropertyName "contentVector" -NotePropertyValue $resp.data[0].embedding
  ($doc | ConvertTo-Json -Compress -Depth 20) | Add-Content $outputFile

  Write-Host "Embedded: $($doc.id)"
}

Write-Host "Done. Wrote $outputFile"