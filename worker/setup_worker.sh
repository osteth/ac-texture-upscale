#!/usr/bin/env bash
# One-time setup on an Ubuntu GPU machine: installs Vulkan tools and the Real-ESRGAN ncnn-vulkan binary
# into ~/ac-upscale (outside the synced folder, so the binary isn't uploaded back to MEGA).
set -euo pipefail

DEST="$HOME/ac-upscale/realesrgan"
URL="https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.5.0/realesrgan-ncnn-vulkan-20220424-ubuntu.zip"

sudo apt-get update
sudo apt-get install -y unzip curl libvulkan1 libgomp1 vulkan-tools

mkdir -p "$DEST"
curl -L "$URL" -o /tmp/realesrgan.zip
unzip -o /tmp/realesrgan.zip -d "$DEST"
rm /tmp/realesrgan.zip
chmod +x "$DEST/realesrgan-ncnn-vulkan"

echo "--- Vulkan devices (your GPU should be listed) ---"
vulkaninfo --summary 2>/dev/null | grep -E "deviceName|driverName" || echo "vulkaninfo found no GPU: install your GPU's Vulkan driver"
echo "--- Smoke test ---"
"$DEST/realesrgan-ncnn-vulkan" -i "$DEST/input.jpg" -o /tmp/realesrgan_test.png -n realesrgan-x4plus && echo "OK: /tmp/realesrgan_test.png"
