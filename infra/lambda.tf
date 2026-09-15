# A funcao roda dentro da VPC porque o RDS nao e publico. Ela fica nas mesmas
# subnets privadas do banco: sem rota para a internet, o unico destino que
# importa e a porta 1433.
resource "aws_security_group" "lambda" {
  name        = "${local.nome_funcao}-sg"
  description = "SG da Lambda de autenticacao (acesso ao RDS)"
  vpc_id      = local.vpc_id

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = {
    Name = "${local.nome_funcao}-sg"
  }
}

# A regra entra no SG do RDS a partir daqui (e nao no stack do banco) para nao
# criar dependencia circular: o banco nao precisa saber que a Lambda existe.
# O SG do RDS tem ignore_changes = [ingress] justamente para preservar isto.
resource "aws_vpc_security_group_ingress_rule" "rds_from_lambda" {
  security_group_id            = local.rds_sg_id
  referenced_security_group_id = aws_security_group.lambda.id
  from_port                    = 1433
  to_port                      = 1433
  ip_protocol                  = "tcp"
  description                  = "SQL Server da Lambda de autenticacao"
}

data "aws_iam_policy_document" "lambda_assume_role" {
  statement {
    effect  = "Allow"
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["lambda.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "lambda" {
  name               = "${local.nome_funcao}-role"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume_role.json
}

# Cobre CloudWatch Logs e a criacao/remocao das ENIs exigidas pelo vpc_config.
resource "aws_iam_role_policy_attachment" "lambda_vpc_access" {
  role       = aws_iam_role.lambda.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaVPCAccessExecutionRole"
}

# Criado explicitamente para controlar a retencao - o log group implicito da
# Lambda nasce com retencao infinita.
resource "aws_cloudwatch_log_group" "lambda" {
  name              = "/aws/lambda/${local.nome_funcao}"
  retention_in_days = var.log_retention_days
}

resource "aws_lambda_function" "auth" {
  function_name = local.nome_funcao
  role          = aws_iam_role.lambda.arn

  # Runtime gerenciado .NET 8 (a aplicacao principal roda em .NET 9, mas a
  # funcao e independente - ver src/).
  runtime = "dotnet8"
  handler = "MechanicLtda.Auth.Lambda::MechanicLtda.Auth.Lambda.Function::FunctionHandler"

  filename         = var.lambda_package_path
  source_code_hash = filebase64sha256(var.lambda_package_path)

  memory_size = var.lambda_memory_size
  timeout     = var.lambda_timeout

  vpc_config {
    subnet_ids         = local.db_subnet_ids
    security_group_ids = [aws_security_group.lambda.id]
  }

  environment {
    variables = {
      DB_CONNECTION_STRING  = data.aws_ssm_parameter.db_connection_string.value
      JWT_SECRET_KEY        = data.aws_ssm_parameter.jwt_secret_key.value
      JWT_ISSUER            = var.jwt_issuer
      JWT_AUDIENCE          = var.jwt_audience
      JWT_EXPIRACAO_MINUTOS = var.jwt_expiracao_minutos

      # Autenticacao por CPF: a chave do indice cego e a mesma usada pela
      # aplicacao (ela grava Clientes.CpfCnpjHash, a funcao consulta por ele).
      CPF_HASH_KEY              = data.aws_ssm_parameter.cpf_hash_key.value
      JWT_ISSUER_CPF            = var.jwt_issuer_cpf
      JWT_CPF_EXPIRACAO_MINUTOS = var.jwt_cpf_expiracao_minutos
    }
  }

  depends_on = [
    aws_iam_role_policy_attachment.lambda_vpc_access,
    aws_cloudwatch_log_group.lambda,
  ]

  tags = {
    Name = local.nome_funcao
  }
}

# ── Lambda authorizer ────────────────────────────────────────────────────────
# Fica FORA da VPC: só valida o token (assinatura, exp, iss, aud) e a role
# permitida na rota, sem I/O. Sem ENI, o cold start é bem menor — e ela entra no
# caminho de toda requisição protegida.

resource "aws_iam_role" "authorizer" {
  name               = "${local.nome_funcao}-authorizer-role"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume_role.json
}

resource "aws_iam_role_policy_attachment" "authorizer_logs" {
  role       = aws_iam_role.authorizer.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
}

resource "aws_cloudwatch_log_group" "authorizer" {
  name              = "/aws/lambda/${local.nome_funcao}-authorizer"
  retention_in_days = var.log_retention_days
}

resource "aws_lambda_function" "authorizer" {
  function_name = "${local.nome_funcao}-authorizer"
  role          = aws_iam_role.authorizer.arn

  # Mesmo pacote da função de autenticação, outro handler: um build, um zip.
  runtime = "dotnet8"
  handler = "MechanicLtda.Auth.Lambda::MechanicLtda.Auth.Lambda.Authorizer::FunctionHandler"

  filename         = var.lambda_package_path
  source_code_hash = filebase64sha256(var.lambda_package_path)

  memory_size = var.authorizer_memory_size
  timeout     = var.authorizer_timeout

  environment {
    variables = {
      JWT_SECRET_KEY = data.aws_ssm_parameter.jwt_secret_key.value
      JWT_ISSUER     = var.jwt_issuer
      JWT_ISSUER_CPF = var.jwt_issuer_cpf
      JWT_AUDIENCE   = var.jwt_audience

      # Rotas em que um token de cliente é aceito (separadas por ";"). Qualquer
      # outra rota de /api exige token administrativo.
      ROTAS_DO_CLIENTE = join(";", var.rotas_do_cliente)
    }
  }

  depends_on = [
    aws_iam_role_policy_attachment.authorizer_logs,
    aws_cloudwatch_log_group.authorizer,
  ]

  tags = {
    Name = "${local.nome_funcao}-authorizer"
  }
}
