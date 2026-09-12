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
