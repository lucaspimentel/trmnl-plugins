$deviceId = [Environment]::GetEnvironmentVariable("TRMNL_DEVICE_ID")
$deviceApiKey = [Environment]::GetEnvironmentVariable("TRMNL_DEVICE_API_KEY")

$headers = @{
    "ID" = $deviceId
    "Access-Token" = $deviceApiKey
}

$response = Invoke-RestMethod -Uri "https://usetrmnl.com/api/current_screen" -Method Get -Headers $headers

if ($null -eq $response -or -not $response.image_url) {
    Write-Host "No image URL found."
    return
}

Write-Host "Image URL: $($response.image_url)"

$imagePath = "{0:yyyy-MM-dd_HH-mm-ss}.png" -f (Get-Date)
$imagePath = Join-Path -Path $PSScriptRoot -ChildPath $imagePath
Invoke-WebRequest -Uri $response.image_url -OutFile $imagePath
Write-Host "Image downloaded to ""$imagePath"""

# Assuming Sixel encoding functionality is available in the environment
# Convert the image to black and white and encode as 2-bit (black/white) sixel
Add-Type -AssemblyName System.Drawing

# Load the image
$bitmap = [System.Drawing.Image]::FromFile($imagePath)

# Convert to black and white 2-bit
$bwBitmap = New-Object System.Drawing.Bitmap $bitmap.Width, $bitmap.Height
for ($y = 0; $y -lt $bitmap.Height; $y++) {
    for ($x = 0; $x -lt $bitmap.Width; $x++) {
        $color = $bitmap.GetPixel($x, $y)
        $luminance = ($color.R * 0.299) + ($color.G * 0.587) + ($color.B * 0.114)
        if ($luminance -gt 127) {
            $bwBitmap.SetPixel($x, $y, [System.Drawing.Color]::White)
        } else {
            $bwBitmap.SetPixel($x, $y, [System.Drawing.Color]::Black)
        }
    }
}

# Encode as sixel (simple, monochrome, 2-bit)
function Convert-BitmapToSixel {
    param([System.Drawing.Bitmap]$bmp)
    $sixel = "`ePq" # Sixel header
    for ($x = 0; $x -lt $bmp.Width; $x++) {
        $col = ""
        for ($y = 0; $y -lt $bmp.Height; $y += 6) {
            $byte = 0
            for ($b = 0; $b -lt 6; $b++) {
                if (($y + $b) -ge $bmp.Height) { break }
                $pixel = $bmp.GetPixel($x, $y + $b)
                if ($pixel.R -eq 0 -and $pixel.G -eq 0 -and $pixel.B -eq 0) {
                    $byte = $byte -bor (1 -shl $b)
                }
            }
            $col += [char](63 + $byte)
        }
        $sixel += $col + "-"
    }
    $sixel += "`e\"
    return $sixel
}

$sixel = Convert-BitmapToSixel -bmp $bwBitmap
Write-Host "`n$sixel`n"