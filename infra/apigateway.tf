# HTTP API (v2): mais barato e mais simples que o REST API v1, e suficiente
# para uma unica rota publica de login.
resource "aws_apigatewayv2_api" "auth" {
  name          = "${local.nome_funcao}-api"
  protocol_type = "HTTP"
  description   = "Autenticacao serverless do MechanicLtda"

  cors_configuration {
    allow_origins = ["*"]
    allow_methods = ["POST", "OPTIONS"]
    allow_headers = ["content-type"]
  }
}

resource "aws_apigatewayv2_integration" "auth" {
  api_id                 = aws_apigatewayv2_api.auth.id
  integration_type       = "AWS_PROXY"
  integration_uri        = aws_lambda_function.auth.invoke_arn
  payload_format_version = "2.0"
}

resource "aws_apigatewayv2_route" "login" {
  api_id    = aws_apigatewayv2_api.auth.id
  route_key = "POST /auth/login"
  target    = "integrations/${aws_apigatewayv2_integration.auth.id}"
}

# Autenticacao do cliente por CPF (requisito da Fase 3). Mesma funcao do login:
# ela despacha pela rota recebida, o que evita um segundo cold start e mantem um
# unico pacote e um unico conjunto de segredos.
#
# Rota publica por definicao: e ela que troca o CPF pelo token, entao nao ha
# credencial a exigir antes. A protecao fica no throttling proprio da rota
# (route_settings do stage), na resposta 401 identica para CPF inexistente e
# cliente inativo e no alarme de 4xx. O nome "login_cpf" marca o recurso como
# endpoint de autenticacao, que e como a regra terraform:S6333 do Sonar
# reconhece as rotas que precisam ser publicas - nao renomear.
resource "aws_apigatewayv2_route" "login_cpf" {
  api_id    = aws_apigatewayv2_api.auth.id
  route_key = "POST /auth/cpf"
  target    = "integrations/${aws_apigatewayv2_integration.auth.id}"
}

resource "aws_apigatewayv2_stage" "default" {
  # Stage $default de proposito: numa integracao privada com stage nomeado, o
  # nome do stage entra no path enviado ao ALB, e as rotas da API mudariam.
  api_id      = aws_apigatewayv2_api.auth.id
  name        = "$default"
  auto_deploy = true

  # Metricas por rota (latencia, contagem, 4xx e 5xx) - base do painel de
  # observabilidade e dos alarmes.
  default_route_settings {
    detailed_metrics_enabled = true
    throttling_rate_limit    = var.throttling_rate_limit
    throttling_burst_limit   = var.throttling_burst_limit
  }

  # As rotas de autenticacao recebem um limite bem mais baixo: sao publicas e
  # trocam credencial por token, entao concentram o risco de forca bruta e de
  # enumeracao de CPF.
  dynamic "route_settings" {
    for_each = toset(["POST /auth/login", "POST /auth/cpf", "POST /api/auth/login"])

    content {
      route_key                = route_settings.value
      detailed_metrics_enabled = true
      throttling_rate_limit    = var.throttling_auth_rate_limit
      throttling_burst_limit   = var.throttling_auth_burst_limit
    }
  }

  # route_settings cita as rotas pelo texto do route_key, sem referencia ao
  # recurso: sem o depends_on o Terraform pode atualizar o stage antes de criar
  # a rota, e a AWS recusa com "Unable to find Route by key".
  depends_on = [
    aws_apigatewayv2_route.login,
    aws_apigatewayv2_route.login_cpf,
    aws_apigatewayv2_route.publicas,
  ]

  access_log_settings {
    destination_arn = aws_cloudwatch_log_group.api.arn
    format = jsonencode({
      requestId          = "$context.requestId"
      rota               = "$context.routeKey"
      metodo             = "$context.httpMethod"
      path               = "$context.path"
      status             = "$context.status"
      latencia           = "$context.responseLatency"
      latenciaIntegracao = "$context.integrationLatency"
      ip                 = "$context.identity.sourceIp"
      erro               = "$context.integrationErrorMessage"
      erroAuthorizer     = "$context.authorizer.error"
    })
  }
}

resource "aws_cloudwatch_log_group" "api" {
  name              = "/aws/apigateway/${local.nome_funcao}"
  retention_in_days = var.log_retention_days
}

resource "aws_lambda_permission" "api_gateway" {
  statement_id  = "AllowInvokeFromApiGateway"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.auth.function_name
  principal     = "apigateway.amazonaws.com"

  # Restringe a esta API (qualquer stage/metodo dela), nao ao API Gateway
  # inteiro da conta.
  source_arn = "${aws_apigatewayv2_api.auth.execution_arn}/*/*"
}
