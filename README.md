# Lambda — Autenticação serverless do MechanicLtda

Function serverless (.NET 8) exposta por **API Gateway HTTP API** que autentica o usuário direto
nas tabelas do ASP.NET Core Identity e devolve um **JWT idêntico ao que a API emite** — mesmo
issuer, audience, claims e chave de assinatura.

```text
POST https://<api-id>.execute-api.us-east-1.amazonaws.com/auth/login
{ "email": "maria@oficina.com", "senha": "Senha@123" }

200 → { "token": "eyJhbGciOi...", "expiracao": "2026-09-12T01:30:00Z" }
401 → { "mensagem": "Credenciais invalidas." }
```

O token serve para chamar a API no cluster sem passar pelo endpoint `/api/v1/auth/login` dela.

## Os quatro repositórios do projeto

| # | Repositório | Conteúdo | CI/CD |
|---|---|---|---|
| 1 | **Lambda** (este) | Função .NET 8 + Terraform da própria função | build, testes e deploy |
| 2 | [InfraKubernete](https://github.com/Fiap-Mecanic-Ltda/InfraKubernete) | Terraform do cluster + manifestos `k8s/` | `terraform plan/apply` + deploy no k3s |
| 3 | [InfraSGBD](https://github.com/Fiap-Mecanic-Ltda/InfraSGBD) | Terraform do RDS SQL Server | `terraform plan/apply` |
| 4 | [MechanicLtda](https://github.com/Fiap-Mecanic-Ltda/MechanicLtda) | Aplicação .NET 9 | build, testes e push das imagens no ECR |

## Como este stack conversa com os outros

```text
InfraKubernete  ──(remote state)──▶  vpc_id, db_subnet_ids
InfraSGBD       ──(remote state)──▶  rds_security_group_id, nome do parâmetro SSM
SSM Parameter Store ─────────────▶  connection string + chave JWT (lidas no apply)
                                     │
                                     ▼
                        API Gateway → Lambda → RDS
```

- A função roda **dentro da VPC**, nas mesmas subnets privadas do banco, com um security group
  próprio; a regra que libera a porta 1433 é criada **aqui**
  (`aws_vpc_security_group_ingress_rule` apontando para o SG do RDS), não no repositório 3 — assim
  não há dependência circular entre os stacks.
- Os segredos são lidos do **SSM no momento do `apply`** e injetados como variáveis de ambiente da
  função. Aquelas subnets não têm rota para a internet nem VPC endpoints, então uma chamada ao SSM
  em runtime ficaria pendurada até o timeout. As variáveis de ambiente da Lambda são cifradas em
  repouso com KMS.

## Estrutura

```text
Lambda/
├── src/MechanicLtda.Auth.Lambda/
│   ├── Function.cs              # handler do API Gateway (payload v2)
│   ├── UsuarioRepository.cs     # consulta AspNetUsers/AspNetRoles (SqlClient)
│   ├── JwtTokenService.cs       # gera o token igual ao AuthAppService da API
│   └── Models.cs                # contratos de entrada/saída e o enum de tipo
├── tests/MechanicLtda.Auth.Lambda.Tests/
│   ├── JwtTokenServiceTests.cs  # o token passa na mesma validação da API
│   └── SenhaIdentityTests.cs    # compatibilidade com os hashes do Identity
├── infra/                       # Terraform: função, API Gateway, IAM, SG, logs
├── scripts/build.sh             # publica e empacota em build/function.zip
└── .github/workflows/ci-cd.yml
```

### Por que .NET 8 e não .NET 9

`dotnet8` é o runtime gerenciado da AWS Lambda. A função é **independente** da aplicação (não
referencia os projetos .NET 9): o contrato entre as duas é o banco e a chave JWT. O que precisa
ficar em sincronia está coberto por testes — claims, issuer/audience e o formato de hash do
Identity.

## Rodando localmente

```bash
dotnet test MechanicLtda.Auth.sln     # build + testes
./scripts/build.sh                    # gera build/function.zip
```

Para planejar/aplicar a infraestrutura (o zip precisa existir antes — o Terraform calcula o
`source_code_hash` a partir dele):

```bash
cd infra
terraform init
terraform plan
terraform apply
terraform output auth_endpoint
```

## Pipeline

`.github/workflows/ci-cd.yml`, em três etapas:

1. **build-and-test** — `dotnet build` + `dotnet test`, depois `scripts/build.sh`; o zip vira
   artefato do workflow.
2. **plan** — baixa o artefato e roda `fmt -check`, `init`, `validate` e `plan`.
3. **apply** — só via `workflow_dispatch` com `action = apply`, protegido pelo Environment
   `production`; ao final imprime o endpoint publicado.

### Secrets necessários

| Nome | Tipo | Uso |
|---|---|---|
| `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` | Secret | Chave do `terraform-deployer` (também lê os states remotos dos repositórios 2 e 3) |

O Environment `production` precisa existir para o job de `apply`.

## Ordem de subida

1. `terraform apply` no **InfraKubernete** (VPC e subnets).
2. `terraform apply` no **InfraSGBD** (RDS + connection string no SSM).
3. `apply` **aqui** — a função só sobe depois que os dois states acima existem, porque lê os
   outputs deles.

## Fluxo de trabalho no Git

A branch de trabalho é **`homologacao`**; nada é commitado direto na `main` — a `main` recebe
mudanças por Pull Request. O workflow roda build/testes/plan nas duas branches; o `apply` é sempre
manual.
