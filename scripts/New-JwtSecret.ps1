# Случайная строка для Jwt__SecretKey (Railway / локально). Скопируйте вывод в Variables.
$bytes = New-Object byte[] 48
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$secret = [Convert]::ToBase64String($bytes)
Write-Host "Jwt__SecretKey=$secret" -ForegroundColor Cyan
