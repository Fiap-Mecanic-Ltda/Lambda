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

resource "aws_apigatewayv2_stage" "default" {
  api_id      = aws_apigatewayv2_api.auth.id
  name        = "$default"
  auto_deploy = true

  access_log_settings {
    destination_arn = aws_cloudwatch_log_group.api.arn
    format = jsonencode({
      requestId = "$context.requestId"
      rota      = "$context.routeKey"
      status    = "$context.status"
      latencia  = "$context.responseLatency"
      erro      = "$context.integrationErrorMessage"
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
