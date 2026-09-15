#!/usr/bin/env bash
# Publica a funcao e empacota em build/function.zip, no layout que o runtime
# gerenciado dotnet8 espera (assembly + deps.json na raiz do zip).
set -euo pipefail

RAIZ="$(cd "$(dirname "$0")/.." && pwd)"
PROJETO="$RAIZ/src/MechanicLtda.Auth.Lambda"
SAIDA="$RAIZ/build"
PUBLICADO="$SAIDA/publish"

rm -rf "$SAIDA"
mkdir -p "$PUBLICADO"

dotnet publish "$PROJETO" \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained false \
  --output "$PUBLICADO"

# O zip precisa ter os arquivos na raiz, nao dentro de uma pasta. O "zip" nao
# existe no Git Bash do Windows, entao ha um fallback em python (presente nas
# duas plataformas) - o runner do CI usa o caminho do zip mesmo.
if command -v zip >/dev/null 2>&1; then
  ( cd "$PUBLICADO" && zip -qr "$SAIDA/function.zip" . )
else
  python -c "
import os, sys, zipfile
origem, destino = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(destino, 'w', zipfile.ZIP_DEFLATED) as z:
    for pasta, _, arquivos in os.walk(origem):
        for arquivo in arquivos:
            completo = os.path.join(pasta, arquivo)
            z.write(completo, os.path.relpath(completo, origem).replace(os.sep, '/'))
" "$PUBLICADO" "$SAIDA/function.zip"
fi

echo "pacote gerado em $SAIDA/function.zip"
