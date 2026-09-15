variable "aws_region" {
  type    = string
  default = "us-east-1"
}

variable "project_name" {
  type    = string
  default = "mechanicltda"
}

variable "environment" {
  type    = string
  default = "prod"
}

# States dos outros stacks (rede e banco), lidos em data.tf.

variable "state_bucket" {
  type        = string
  description = "Bucket S3 onde ficam os states dos demais stacks."
  default     = "mechanicltda-terraform-state-788516091173"
}

variable "kubernetes_state_key" {
  type        = string
  description = "State do repositorio InfraKubernete (VPC, subnets, SGs dos nos)."
  default     = "prod/terraform.tfstate"
}

variable "sgbd_state_key" {
  type        = string
  description = "State do repositorio InfraSGBD (RDS e seu security group)."
  default     = "prod/sgbd/terraform.tfstate"
}

# Pacote da funcao

variable "lambda_package_path" {
  type        = string
  description = "Caminho do zip publicado pelo build (scripts/build.sh gera em build/function.zip)."
  default     = "../build/function.zip"
}

variable "lambda_memory_size" {
  type        = number
  description = "Memoria da funcao em MB - tambem define a fatia de CPU."
  default     = 512
}

variable "lambda_timeout" {
  type        = number
  description = "Timeout em segundos. Cobre o cold start com conexao ao RDS."
  default     = 30
}

variable "log_retention_days" {
  type    = number
  default = 14
}

# JWT - precisam bater com o appsettings.json da API, senao o token emitido
# pela Lambda e rejeitado la.

variable "jwt_issuer" {
  type    = string
  default = "MechanicLtda.API"
}

variable "jwt_audience" {
  type    = string
  default = "MechanicLtda.Clients"
}

variable "jwt_expiracao_minutos" {
  type    = number
  default = 60
}

# Autenticação por CPF

variable "jwt_issuer_cpf" {
  type        = string
  description = <<-EOT
    Emissor dos tokens da autenticação por CPF. Precisa bater com
    JwtSettings:IssuerCpf no appsettings da API — é o segundo issuer que ela
    aceita, ao lado do emissor do login por e-mail e senha.
  EOT
  default     = "MechanicLtda.Auth.Cpf"
}

variable "jwt_cpf_expiracao_minutos" {
  type        = number
  description = <<-EOT
    Validade do token obtido por CPF. Menor que a do token de usuário: aqui a
    credencial é apenas o CPF, sem senha.
  EOT
  default     = 30
}

# Authorizer e integração com o cluster

variable "authorizer_memory_size" {
  type        = number
  description = "Memoria do authorizer em MB. So valida token, nao faz I/O."
  default     = 256
}

variable "authorizer_timeout" {
  type        = number
  description = "Timeout do authorizer em segundos."
  default     = 5
}

variable "authorizer_cache_ttl" {
  type        = number
  description = <<-EOT
    Cache da decisão do authorizer, em segundos, indexado por
    (Authorization, routeKey). Reduz invocação e latência nas chamadas
    seguintes do mesmo token na mesma rota.
  EOT
  default     = 300
}

variable "rotas_do_cliente" {
  type        = list(string)
  description = <<-EOT
    Rotas em que um token com role Cliente é aceito. Em qualquer outra rota de
    /api o authorizer exige token administrativo. A posse do recurso (o
    clienteId da rota ser o do token) é verificada pela API.
  EOT
  default     = ["GET /api/ordemservico/cliente/{clienteId}"]
}

variable "integracao_timeout_ms" {
  type        = number
  description = "Timeout da integracao privada com o ALB, em milissegundos (maximo 30000)."
  default     = 29000
}

# Throttling do stage

variable "throttling_rate_limit" {
  type        = number
  description = "Requisicoes por segundo no limite padrao do stage."
  default     = 50
}

variable "throttling_burst_limit" {
  type        = number
  description = "Rajada no limite padrao do stage."
  default     = 100
}

variable "throttling_auth_rate_limit" {
  type        = number
  description = <<-EOT
    Requisições por segundo nas rotas de autenticação. Limite baixo de
    propósito: são públicas e trocam credencial por token, então é onde mora o
    risco de força bruta e de enumeração de CPF.
  EOT
  default     = 5
}

variable "throttling_auth_burst_limit" {
  type        = number
  description = "Rajada nas rotas de autenticacao."
  default     = 10
}
