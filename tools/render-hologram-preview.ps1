param([string]$Directory='dist\hologram-qa')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
$folder=(Resolve-Path -LiteralPath $Directory).Path
$encoder=New-Object Windows.Media.Imaging.GifBitmapEncoder
$files=Get-ChildItem -LiteralPath (Join-Path $folder 'motion-frames') -Filter 'frame-*.png' | Sort-Object Name
if ($files.Count -ne 90) { throw 'The native motion preview must contain all 90 frames.' }
foreach ($file in $files) {
    $source=New-Object Windows.Media.Imaging.BitmapImage
    $source.BeginInit()
    $source.CacheOption=[Windows.Media.Imaging.BitmapCacheOption]::OnLoad
    $source.UriSource=[Uri]$file.FullName
    $source.EndInit()
    $metadata=New-Object Windows.Media.Imaging.BitmapMetadata 'gif'
    $metadata.SetQuery('/grctlext/Delay',[UInt16]7)
    $metadata.SetQuery('/grctlext/Disposal',[byte]1)
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($source,$null,$metadata,$null))
}
$path=Join-Path $folder 'hologram-motion.gif'
$stream=[IO.File]::Create($path)
try { $encoder.Save($stream) } finally { $stream.Dispose() }
# Add the standard infinite-loop extension after the global colour table.
$bytes=[IO.File]::ReadAllBytes($path)
$offset=13
if ($bytes[10] -band 128) { $offset+=3*[int][Math]::Pow(2,1+($bytes[10] -band 7)) }
# WIC can discard per-frame delay metadata. Set every graphic-control delay in the encoded stream.
$cursor=$offset
$controls=0
while ($cursor -lt $bytes.Length -and $bytes[$cursor] -ne 59) {
    $marker=$bytes[$cursor]
    if ($marker -eq 33) {
        $label=$bytes[$cursor+1];$cursor+=2
        if ($label -eq 249) { $bytes[$cursor+2]=7;$bytes[$cursor+3]=0;$controls++ }
    } elseif ($marker -eq 44) {
        $packed=$bytes[$cursor+9];$cursor+=10
        if ($packed -band 128) { $cursor+=3*[int][Math]::Pow(2,1+($packed -band 7)) }
        $cursor++ # LZW minimum code size
    } else { throw 'Unexpected block in the encoded GIF.' }
    do {
        $size=$bytes[$cursor];$cursor++
        $cursor+=$size
    } while ($size -ne 0 -and $cursor -lt $bytes.Length)
}
if ($controls -ne 90) { throw 'Every native preview frame must have a graphic-control delay.' }
$loop=[byte[]](33,255,11,78,69,84,83,67,65,80,69,50,46,48,3,1,0,0,0)
$output=New-Object IO.MemoryStream
try {
    $output.Write($bytes,0,$offset)
    $output.Write($loop,0,$loop.Length)
    $output.Write($bytes,$offset,$bytes.Length-$offset)
    [IO.File]::WriteAllBytes($path,$output.ToArray())
} finally { $output.Dispose() }
Write-Output ('Native render preview saved: '+$path+' (90 frames; simulated speech, no paid voice service)')
