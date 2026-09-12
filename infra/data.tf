# Rede e SGs dos nos k3s: stack de infraestrutura de Kubernetes.
data "terraform_remote_state" "kubernetes" {
  backend = "s3"

  config = {
    bucket = var.state_bucket
    key    = var.kubernetes_state_key
    region = var.aws_region
  }
}

# RDS e o security group dele: stack do banco gerenciado.
data "terraform_remote_state" "sgbd" {
  backend = "s3"

  config = {
    bucket = var.state_bucket
    key    = var.sgbd_state_key
    region = var.aws_region
  }
}

# Segredos da aplicacao. Lidos no APPLY (nao em runtime) e injetados como
# variaveis de ambiente da funcao: as subnets de banco nao tem rota para a
# internet nem VPC endpoints, entao uma chamada ao SSM de dentro da VPC ficaria
# pendurada ate o timeout. As variaveis de ambiente da Lambda sao cifradas em
# repouso com KMS.
data "aws_ssm_parameter" "db_connection_string" {
  name            = data.terraform_remote_state.sgbd.outputs.db_connection_ssm_parameter
  with_decryption = true
}

data "aws_ssm_parameter" "jwt_secret_key" {
  name            = "/${var.project_name}/${var.environment}/jwt-secret-key"
  with_decryption = true
}

locals {
  nome_funcao = "${var.project_name}-${var.environment}-auth"

  vpc_id        = data.terraform_remote_state.kubernetes.outputs.vpc_id
  db_subnet_ids = data.terraform_remote_state.kubernetes.outputs.db_subnet_ids
  rds_sg_id     = data.terraform_remote_state.sgbd.outputs.rds_security_group_id
}
