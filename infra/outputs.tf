output "auth_endpoint" {
  description = "URL do login serverless (POST com {\"email\":\"...\",\"senha\":\"...\"})."
  value       = "${aws_apigatewayv2_api.auth.api_endpoint}/auth/login"
}

output "api_endpoint" {
  value = aws_apigatewayv2_api.auth.api_endpoint
}

output "lambda_function_name" {
  value = aws_lambda_function.auth.function_name
}

output "lambda_security_group_id" {
  value = aws_security_group.lambda.id
}

output "auth_cpf_endpoint" {
  description = "URL da autenticacao por CPF (POST com {\"cpf\":\"...\"})."
  value       = "${aws_apigatewayv2_api.auth.api_endpoint}/auth/cpf"
}

output "api_base_url" {
  description = <<-EOT
    Base das rotas da aplicação publicadas pelo gateway (ex.: /api/cliente,
    /health, /swagger). É esta URL que vai em app_base_url_aprovacao no stack
    InfraKubernete e no README da aplicação.
  EOT
  value       = aws_apigatewayv2_api.auth.api_endpoint
}

output "authorizer_function_name" {
  value = aws_lambda_function.authorizer.function_name
}

output "vpc_link_id" {
  value = aws_apigatewayv2_vpc_link.cluster.id
}
