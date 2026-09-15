#!/usr/bin/env bash
# Smoke test do API Gateway publicado: confirma, de fora da AWS, que cada salto
# do caminho está de pé depois de um deploy.
#
#   bash scripts/smoke-test.sh https://<api-id>.execute-api.us-east-1.amazonaws.com
#
# Sem segredo nenhum, cobre: VPC Link -> ALB -> pods (/health), a Lambda de
# autenticação sem e com ida ao banco (/auth/cpf) e o authorizer (sem token e
# com token inválido).
#
# Com SMOKE_CPF (CPF de um cliente ATIVO em produção), cobre também o fluxo que o
# desafio pede para demonstrar: token por CPF, consumo da rota protegida e o
# bloqueio de acesso aos dados de outro cliente.
set -euo pipefail

BASE="${1:?Informe a URL base do gateway (terraform output -raw api_base_url)}"
BASE="${BASE%/}"

falhas=0

codigo() {
  curl -s -o /dev/null -w '%{http_code}' --max-time 30 "$@" || echo "000"
}

verificar() {
  local descricao="$1" esperado="$2" obtido="$3"

  if [ "$obtido" = "$esperado" ]; then
    echo "ok     $descricao -> $obtido"
  else
    echo "FALHOU $descricao -> esperado $esperado, obtido $obtido"
    falhas=$((falhas + 1))
  fi
}

echo "Smoke test em $BASE"

# 1. Backend pelo VPC Link. Com retry: logo depois de criado, o VPC Link e o
#    registro dos alvos no ALB levam alguns minutos para ficarem prontos.
health="000"
for tentativa in $(seq 1 20); do
  health=$(codigo "$BASE/health")
  [ "$health" = "200" ] && break
  echo "       /health respondeu $health (tentativa $tentativa/20), aguardando 15s..."
  sleep 15
done
verificar "GET /health (gateway -> VPC Link -> ALB -> pod)" 200 "$health"

# 2. CPF com dígito verificador errado: 400 sem tocar o banco.
verificar "POST /auth/cpf com CPF invalido" 400 \
  "$(codigo -X POST "$BASE/auth/cpf" -H 'Content-Type: application/json' -d '{"cpf":"123.456.789-00"}')"

# 3. CPF válido que não é cliente: 401. Passa pelo banco, então também prova
#    que a Lambda alcança o RDS e que a coluna CpfCnpjHash existe (sem ela, 500).
verificar "POST /auth/cpf com CPF sem cadastro" 401 \
  "$(codigo -X POST "$BASE/auth/cpf" -H 'Content-Type: application/json' -d '{"cpf":"935.411.347-80"}')"

# 4. Rota protegida sem token: o próprio gateway recusa, sem invocar o authorizer.
verificar "GET /api/cliente sem token" 401 "$(codigo "$BASE/api/cliente")"

# 5. Token inválido: o authorizer nega.
verificar "GET /api/cliente com token invalido" 403 \
  "$(codigo "$BASE/api/cliente" -H 'Authorization: Bearer token-invalido')"

# 6. Fluxo completo do cliente (opcional).
if [ -n "${SMOKE_CPF:-}" ]; then
  resposta=$(curl -s --max-time 30 -X POST "$BASE/auth/cpf" \
    -H 'Content-Type: application/json' -d "{\"cpf\":\"$SMOKE_CPF\"}" || true)

  token=$(printf '%s' "$resposta" | jq -r '.token // empty' 2>/dev/null || true)
  cliente=$(printf '%s' "$resposta" | jq -r '.cliente.id // empty' 2>/dev/null || true)

  if [ -z "$token" ] || [ -z "$cliente" ]; then
    echo "FALHOU POST /auth/cpf com SMOKE_CPF -> sem token na resposta"
    falhas=$((falhas + 1))
  else
    echo "ok     POST /auth/cpf com SMOKE_CPF -> token do cliente $cliente"

    verificar "GET /api/ordemservico/cliente/{proprio id} com token do cliente" 200 \
      "$(codigo "$BASE/api/ordemservico/cliente/$cliente" -H "Authorization: Bearer $token")"

    # Mesma rota, outro cliente: o authorizer libera a rota, a API barra a posse.
    verificar "GET /api/ordemservico/cliente/{outro id} com token do cliente" 403 \
      "$(codigo "$BASE/api/ordemservico/cliente/$((cliente + 100000))" -H "Authorization: Bearer $token")"

    # Rota administrativa: o authorizer barra o token de cliente.
    verificar "GET /api/cliente com token de cliente" 403 \
      "$(codigo "$BASE/api/cliente" -H "Authorization: Bearer $token")"
  fi
else
  echo "pulado fluxo completo do cliente (defina SMOKE_CPF para executar)"
fi

if [ "$falhas" -gt 0 ]; then
  echo "Smoke test: $falhas verificacao(oes) falharam."
  exit 1
fi

echo "Smoke test: tudo ok."
