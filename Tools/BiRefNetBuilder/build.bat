@echo off
REM BiRefNet PyInstaller Build Script
REM Run this from Tools/BiRefNetBuilder folder

SET SCRIPT_DIR=%~dp0
SET STREAMING_ASSETS=%SCRIPT_DIR%..\..\Assets\StreamingAssets\BiRefNet

echo [BUILD] Activating virtual environment...
call .venv\Scripts\activate

echo [BUILD] Installing PyInstaller if not present...
pip install pyinstaller>=6.0.0

echo [BUILD] Building executable...
pyinstaller --noconfirm --onedir --console ^
    --name birefnet_infer ^
    --add-data "models;models" ^
    --hidden-import torch ^
    --hidden-import torchvision ^
    --hidden-import transformers ^
    --hidden-import einops ^
    --hidden-import kornia ^
    --hidden-import timm ^
    --hidden-import PIL ^
    --collect-all torch ^
    --collect-all torchvision ^
    --collect-all transformers ^
    --collect-all timm ^
    --collect-all einops ^
    --collect-all kornia ^
    birefnet_infer.py

if %ERRORLEVEL% NEQ 0 (
    echo [BUILD] Build failed!
    exit /b 1
)

echo [BUILD] Build successful!
echo [BUILD] Copying to StreamingAssets...

REM Remove old dist and copy new one
if exist "%STREAMING_ASSETS%\dist" rmdir /s /q "%STREAMING_ASSETS%\dist"
xcopy /E /I /Y "dist" "%STREAMING_ASSETS%\dist"

echo [BUILD] Done. Exe at: %STREAMING_ASSETS%\dist\birefnet_infer\birefnet_infer.exe
