#!/usr/bin/env bash
# FiveMHater logo -> Assets/fivemhater_logo.png (şeffaf) + Assets/fivemhater.ico (16/24/32/48/64/128/256)
# Kullanım:  tools/make_assets.sh [kaynak_logo.(gif|png|jpg)]
# Kaynak verilmezse gümüş "FH" + kırmızı parlama yedek logosu üretilir.
set -euo pipefail
cd "$(dirname "$0")/.."
A=FiveMHaterOptimizer/Assets
mkdir -p "$A"
IM=$(command -v magick || command -v convert)
FONT=/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf
if [ $# -ge 1 ]; then
  SRC="$1"
  # İlk kare, siyah zemin -> şeffaflık (alpha = max(R,G,B)), kenarları kırp, 512x512 kare tuval
  "$IM" "${SRC}[0]" -alpha off -colorspace sRGB \
     \( +clone -separate -evaluate-sequence max -level 3%,60% \) \
     -compose CopyOpacity -composite -trim +repage \
     -resize 480x480 -background none -gravity center -extent 512x512 \
     "$A/fivemhater_logo.png"
else
  "$IM" -size 512x512 xc:none \
     \( -size 512x512 xc:none -font "$FONT" -pointsize 300 -fill '#E11D2E' -gravity center -annotate +0+10 'FH' -blur 0x22 \) -compose over -composite \
     \( -size 512x512 xc:none -font "$FONT" -pointsize 300 -fill '#FF2A3D' -gravity center -annotate +0+10 'FH' -blur 0x6 \) -compose over -composite \
     \( -size 512x512 gradient:'#FFFFFF-#8E9096' \( -size 512x512 xc:none -font "$FONT" -pointsize 300 -fill white -gravity center -annotate +0+10 'FH' \) -compose CopyOpacity -composite \) -compose over -composite \
     "$A/fivemhater_logo.png"
fi
# Çok katmanlı ICO: kare logoyu koyu zemin üzerine yerleştir (küçük boyutlarda okunabilirlik)
"$IM" "$A/fivemhater_logo.png" -background none -gravity center -resize 256x256 -extent 256x256 \
   -define icon:auto-resize=256,128,64,48,32,24,16 "$A/fivemhater.ico"
"$IM" identify "$A/fivemhater.ico" 2>/dev/null || identify "$A/fivemhater.ico"
