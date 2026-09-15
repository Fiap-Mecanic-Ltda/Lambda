# Rotas da aplicação: API Gateway -> VPC Link -> ALB interno -> NodePort do k3s.
#
# Antes desta fase a API era consumida direto pelo IP público da EC2, em HTTP.
# Com o VPC Link, o backend fica dentro da VPC (o security group dos nós passa a
# aceitar só o ALB, no stack InfraKubernete) e o gateway se torna a única porta
# de entrada — com HTTPS, autenticação por token e throttling.

resource "aws_security_group" "vpc_link" {
  name        = "${local.nome_funcao}-vpclink-sg"
  description = "SG das ENIs do VPC Link do API Gateway"
  vpc_id      = local.vpc_id

  # O destino é o ALB interno (porta 80) dentro da própria VPC. O ingress fica
  # vazio: as ENIs do VPC Link só originam conexões.
  egress {
    description = "HTTP para o ALB interno"
    from_port   = 80
    to_port     = 80
    protocol    = "tcp"
    cidr_blocks = [data.aws_vpc.main.cidr_block]
  }

  tags = {
    Name = "${local.nome_funcao}-vpclink-sg"
  }
}

resource "aws_apigatewayv2_vpc_link" "cluster" {
  name               = "${local.nome_funcao}-vpclink"
  subnet_ids         = local.app_subnet_ids
  security_group_ids = [aws_security_group.vpc_link.id]

  tags = {
    Name = "${local.nome_funcao}-vpclink"
  }
}

# ── Integrações privadas ─────────────────────────────────────────────────────
# Duas, e não uma: o mapeamento de parâmetros é por integração, e só as rotas
# protegidas têm $context.authorizer disponível para repassar ao backend.

resource "aws_apigatewayv2_integration" "cluster_publica" {
  api_id           = aws_apigatewayv2_api.auth.id
  integration_type = "HTTP_PROXY"

  # ANY preserva o método original da requisição.
  integration_method     = "ANY"
  integration_uri        = local.alb_listener_arn
  connection_type        = "VPC_LINK"
  connection_id          = aws_apigatewayv2_vpc_link.cluster.id
  payload_format_version = "1.0"
  timeout_milliseconds   = var.integracao_timeout_ms

  # Correlação: o mesmo id aparece no access log do gateway e nas linhas de log
  # da API (middleware de correlação), o que permite seguir uma requisição do
  # começo ao fim.
  request_parameters = {
    "overwrite:header.X-Correlation-Id" = "$context.requestId"
  }
}

resource "aws_apigatewayv2_integration" "cluster_protegida" {
  api_id                 = aws_apigatewayv2_api.auth.id
  integration_type       = "HTTP_PROXY"
  integration_method     = "ANY"
  integration_uri        = local.alb_listener_arn
  connection_type        = "VPC_LINK"
  connection_id          = aws_apigatewayv2_vpc_link.cluster.id
  payload_format_version = "1.0"
  timeout_milliseconds   = var.integracao_timeout_ms

  # X-Cliente-Id e X-User-Role são informativos (log e rastreio). A decisão de
  # autorização continua sendo tomada pela API a partir do próprio JWT: header
  # de gateway não substitui token validado.
  request_parameters = {
    "overwrite:header.X-Correlation-Id" = "$context.requestId"
    "overwrite:header.X-Cliente-Id"     = "$context.authorizer.clienteId"
    "overwrite:header.X-User-Role"      = "$context.authorizer.role"
  }
}

# ── Lambda authorizer ────────────────────────────────────────────────────────

resource "aws_apigatewayv2_authorizer" "jwt" {
  api_id                            = aws_apigatewayv2_api.auth.id
  authorizer_type                   = "REQUEST"
  name                              = "${local.nome_funcao}-jwt"
  authorizer_uri                    = aws_lambda_function.authorizer.invoke_arn
  authorizer_payload_format_version = "2.0"

  # Resposta simples: a função devolve { isAuthorized, context }.
  enable_simple_responses = true

  # O cache é indexado pelas identity sources. Por isso o routeKey entra aqui:
  # a decisão depende da rota (token de cliente não vale em rota
  # administrativa), e sem ele o gateway reaproveitaria a decisão de uma rota
  # em outra.
  identity_sources = ["$request.header.Authorization", "$context.routeKey"]

  authorizer_result_ttl_in_seconds = var.authorizer_cache_ttl
}

resource "aws_lambda_permission" "authorizer" {
  statement_id  = "AllowInvokeFromApiGatewayAuthorizer"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.authorizer.function_name
  principal     = "apigateway.amazonaws.com"
  source_arn    = "${aws_apigatewayv2_api.auth.execution_arn}/authorizers/${aws_apigatewayv2_authorizer.jwt.id}"
}

# ── Rotas públicas da aplicação ──────────────────────────────────────────────
# Ficam sem authorizer por natureza: o login troca credencial por token, os
# links de aprovação de OS chegam por e-mail (o próprio token da URL é a
# credencial), e health e Swagger não expõem dado de cliente.

locals {
  rotas_publicas = {
    login_api         = "POST /api/auth/login"
    aprovacao_aprovar = "GET /api/aprovacaoordemservico/{token}/aprovar"
    aprovacao_recusar = "GET /api/aprovacaoordemservico/{token}/recusar"
    health            = "GET /health"
    swagger           = "GET /swagger"
    swagger_arquivos  = "GET /swagger/{proxy+}"
  }
}

resource "aws_apigatewayv2_route" "publicas" {
  for_each = local.rotas_publicas

  api_id    = aws_apigatewayv2_api.auth.id
  route_key = each.value
  target    = "integrations/${aws_apigatewayv2_integration.cluster_publica.id}"
}

# ── Rotas protegidas ─────────────────────────────────────────────────────────
# A rota do cliente é declarada à parte (e não só coberta pelo {proxy+}) porque
# o authorizer decide por routeKey: é o que permite liberar o cliente nela e
# barrá-lo em todo o resto de /api.

resource "aws_apigatewayv2_route" "ordens_do_cliente" {
  api_id             = aws_apigatewayv2_api.auth.id
  route_key          = "GET /api/ordemservico/cliente/{clienteId}"
  target             = "integrations/${aws_apigatewayv2_integration.cluster_protegida.id}"
  authorization_type = "CUSTOM"
  authorizer_id      = aws_apigatewayv2_authorizer.jwt.id
}

resource "aws_apigatewayv2_route" "api_protegida" {
  api_id             = aws_apigatewayv2_api.auth.id
  route_key          = "ANY /api/{proxy+}"
  target             = "integrations/${aws_apigatewayv2_integration.cluster_protegida.id}"
  authorization_type = "CUSTOM"
  authorizer_id      = aws_apigatewayv2_authorizer.jwt.id
}
