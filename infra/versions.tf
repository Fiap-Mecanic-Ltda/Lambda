terraform {
  required_version = ">= 1.10.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }

  # Mesmo bucket dos outros stacks, chave propria: a Lambda pode ser aplicada e
  # destruida sem tocar no cluster nem no banco.
  backend "s3" {
    bucket       = "mechanicltda-terraform-state-788516091173"
    key          = "prod/lambda/terraform.tfstate"
    region       = "us-east-1"
    encrypt      = true
    use_lockfile = true
  }
}

provider "aws" {
  region = var.aws_region
}
