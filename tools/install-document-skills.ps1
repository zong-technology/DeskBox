param([string]$Python = 'python', [switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
& $Python -c "import sys; assert sys.version_info >= (3,12), 'Python 3.12+ required'"
if ($LASTEXITCODE -ne 0) { throw 'Install Python 3.12 or later before installing document skills.' }
if (-not $CheckOnly) {
    & $Python -m pip install 'openpyxl>=3.1,<4' 'PyMuPDF>=1.24,<2' 'qrcode[pil]>=8,<9'
    if ($LASTEXITCODE -ne 0) { throw 'Document dependency installation failed.' }
}
& $Python -I -c "import openpyxl,pymupdf,qrcode; print('AI&SI document and QR runtime ready')"
if ($LASTEXITCODE -ne 0) { throw 'Document runtime check failed.' }
