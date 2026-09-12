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
  default     = "mechanicltda-terraform-state-430606112709"
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
