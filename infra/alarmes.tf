# Alarmes do caminho de autenticação e da entrada do gateway.
#
# A observabilidade da aplicação está no New Relic (repositório InfraKubernete), mas o
# API Gateway e as Lambdas só aparecem lá com a integração AWS da conta. Estes alarmes
# cobrem esse trecho direto no CloudWatch: erro 5xx e latência na entrada, erro e
# throttling nas funções e pico de 4xx em /auth/cpf, que é o sinal de força bruta ou
# de enumeração de CPF.

resource "aws_sns_topic" "alarmes" {
  name = "${local.nome_funcao}-alarmes"
}

# Opcional. A AWS envia um e-mail de confirmação: sem clicar no link, nada chega.
resource "aws_sns_topic_subscription" "email" {
  count = var.email_alarmes != "" ? 1 : 0

  topic_arn = aws_sns_topic.alarmes.arn
  protocol  = "email"
  endpoint  = var.email_alarmes
}

locals {
  # HTTP API publica as métricas com as dimensões ApiId e Stage; as de rota (detailed
  # metrics) acrescentam Method e Resource.
  dimensoes_gateway = {
    ApiId = aws_apigatewayv2_api.auth.id
    Stage = aws_apigatewayv2_stage.default.name
  }

  funcoes_monitoradas = {
    autenticacao = aws_lambda_function.auth.function_name
    authorizer   = aws_lambda_function.authorizer.function_name
  }
}

# ── API Gateway ──────────────────────────────────────────────────────────────

resource "aws_cloudwatch_metric_alarm" "gateway_5xx" {
  alarm_name        = "${local.nome_funcao}-gateway-5xx"
  alarm_description = "Respostas 5xx no API Gateway: backend fora (VPC Link, ALB, pods) ou Lambda falhando."
  namespace         = "AWS/ApiGateway"
  metric_name       = "5xx"
  dimensions        = local.dimensoes_gateway
  statistic         = "Sum"

  period              = 300
  evaluation_periods  = 1
  comparison_operator = "GreaterThanThreshold"
  threshold           = var.alarme_gateway_5xx_limite

  # Sem tráfego não há datapoint, e silêncio não é incidente.
  treat_missing_data = "notBreaching"

  alarm_actions = [aws_sns_topic.alarmes.arn]
  ok_actions    = [aws_sns_topic.alarmes.arn]
}

resource "aws_cloudwatch_metric_alarm" "gateway_latencia" {
  alarm_name         = "${local.nome_funcao}-gateway-latencia-p95"
  alarm_description  = "Latencia p95 do API Gateway (entrada ate a resposta) acima do limite por 5 minutos seguidos."
  namespace          = "AWS/ApiGateway"
  metric_name        = "Latency"
  dimensions         = local.dimensoes_gateway
  extended_statistic = "p95"

  period              = 60
  evaluation_periods  = 5
  datapoints_to_alarm = 5
  comparison_operator = "GreaterThanThreshold"
  threshold           = var.alarme_latencia_p95_ms
  treat_missing_data  = "notBreaching"

  alarm_actions = [aws_sns_topic.alarmes.arn]
  ok_actions    = [aws_sns_topic.alarmes.arn]
}

resource "aws_cloudwatch_metric_alarm" "auth_cpf_4xx" {
  alarm_name        = "${local.nome_funcao}-auth-cpf-4xx"
  alarm_description = "Muitas respostas 4xx em POST /auth/cpf em 5 minutos: tentativa de forca bruta ou enumeracao de CPF (inclui 429 do throttling)."
  namespace         = "AWS/ApiGateway"
  metric_name       = "4xx"
  statistic         = "Sum"

  dimensions = merge(local.dimensoes_gateway, {
    Method   = "POST"
    Resource = "/auth/cpf"
  })

  period              = 300
  evaluation_periods  = 1
  comparison_operator = "GreaterThanThreshold"
  threshold           = var.alarme_auth_cpf_4xx_limite
  treat_missing_data  = "notBreaching"

  alarm_actions = [aws_sns_topic.alarmes.arn]
  ok_actions    = [aws_sns_topic.alarmes.arn]
}

# ── Lambdas ──────────────────────────────────────────────────────────────────

resource "aws_cloudwatch_metric_alarm" "lambda_erros" {
  for_each = local.funcoes_monitoradas

  alarm_name        = "${each.value}-erros"
  alarm_description = "Invocacoes com erro nao tratado na funcao ${each.value}. Na autenticacao, costuma ser o RDS inacessivel; no authorizer, configuracao de chave ou issuer."
  namespace         = "AWS/Lambda"
  metric_name       = "Errors"
  dimensions        = { FunctionName = each.value }
  statistic         = "Sum"

  period              = 300
  evaluation_periods  = 1
  comparison_operator = "GreaterThanThreshold"
  threshold           = 0
  treat_missing_data  = "notBreaching"

  alarm_actions = [aws_sns_topic.alarmes.arn]
  ok_actions    = [aws_sns_topic.alarmes.arn]
}

resource "aws_cloudwatch_metric_alarm" "lambda_throttles" {
  for_each = local.funcoes_monitoradas

  alarm_name        = "${each.value}-throttles"
  alarm_description = "Invocacoes recusadas por limite de concorrencia na funcao ${each.value}: autenticacao ou rotas protegidas respondendo erro para o cliente."
  namespace         = "AWS/Lambda"
  metric_name       = "Throttles"
  dimensions        = { FunctionName = each.value }
  statistic         = "Sum"

  period              = 300
  evaluation_periods  = 1
  comparison_operator = "GreaterThanThreshold"
  threshold           = 0
  treat_missing_data  = "notBreaching"

  alarm_actions = [aws_sns_topic.alarmes.arn]
  ok_actions    = [aws_sns_topic.alarmes.arn]
}
