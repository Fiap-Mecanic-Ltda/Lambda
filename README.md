# Lambda — Autenticação serverless e API Gateway do MechanicLtda

Functions serverless (.NET 8) e o **API Gateway HTTP API** que é a porta de entrada da
aplicação. Duas formas de autenticar, as duas devolvendo um **JWT que a API aceita** — mesma
chave de assinatura, mesma audience:

```text
POST https://<api-id>.execute-api.us-east-1.amazonaws.com/auth/login
{ "email": "maria@oficina.com", "senha": "Senha@123" }

200 → { "token": "eyJhbGciOi...", "expiracao": "2026-09-12T01:30:00Z" }
401 → { "mensagem": "Credenciais invalidas." }
```

```text
POST https://<api-id>.execute-api.us-east-1.amazonaws.com/auth/cpf
{ "cpf": "529.982.247-25" }

200 → { "token": "eyJhbGciOi...", "expiracao": "...", "cliente": { "id": 42, "nome": "Fernanda Lima" } }
400 → { "mensagem": "CPF invalido." }                                    (dígitos verificadores)
401 → { "mensagem": "Nao foi possivel autenticar com o CPF informado." } (inexistente ou inativo)
```

## O que o gateway publica

| Rota | Destino | Autorização |
|---|---|---|
| `POST /auth/login` | Lambda (e-mail e senha, tabelas do Identity) | pública |
| `POST /auth/cpf` | Lambda (CPF do cliente) | pública, com throttling menor |
| `POST /api/auth/login` | API no cluster | pública |
| `GET /api/aprovacaoordemservico/{token}/aprovar` e `/recusar` | API no cluster | pública (o token do e-mail é a credencial) |
| `GET /health`, `GET /swagger`, `GET /swagger/{proxy+}` | API no cluster | pública |
| `GET /api/ordemservico/cliente/{clienteId}` | API no cluster | authorizer — aceita token de cliente |
| `ANY /api/{proxy+}` | API no cluster | authorizer — só token administrativo |

As rotas da aplicação chegam ao cluster por **VPC Link → ALB interno → NodePort 8080**, e não
mais pelo IP público da EC2.

O authorizer faz **autenticação** (assinatura, `exp`, `aud` e os dois issuers) e o **filtro de
role por rota**. A **posse do recurso** — o `clienteId` da rota ser o do token — é verificada
pela API: a resposta do authorizer é cacheada por (`Authorization`, `routeKey`), e uma decisão
que dependesse do valor do path seria reaproveitada para outro `clienteId` enquanto o cache
valesse.

### Autenticação por CPF, em detalhe

1. Valida os dígitos verificadores — CPF malformado é `400` e não custa uma ida ao banco.
2. Calcula o **índice cego** do CPF (HMAC-SHA256 dos dígitos) e consulta
   `Clientes.CpfCnpjHash`. A coluna `CpfCnpj` é cifrada com IV aleatório, logo não é
   pesquisável por igualdade; o hash resolve isso sem que a função conheça a chave de
   criptografia. A chave do HMAC (`CPF_HASH_KEY`) é a mesma configurada na aplicação — o vetor
   em `DocumentoHashTests` é idêntico ao do teste da API justamente para travar isso.
3. Cliente inexistente e cliente inativo devolvem a **mesma** resposta `401`: distinguir os
   dois permitiria descobrir, um CPF por vez, quem é cliente da oficina. O CPF não vai para o
   log nem para o token.
4. O token carrega `sub`, `clienteId`, `role = Cliente` e `tipo = Cliente`, com validade curta
   (30 min por padrão) e emissor próprio (`MechanicLtda.Auth.Cpf`), aceito pela API como
   segundo issuer.

## Os quatro repositórios do projeto

| # | Repositório | Conteúdo | CI/CD |
|---|---|---|---|
| 1 | **Lambda** (este) | Função .NET 8 + Terraform da própria função | build, testes e deploy |
| 2 | [InfraKubernete](https://github.com/Fiap-Mecanic-Ltda/InfraKubernete) | Terraform do cluster + manifestos `k8s/` | `terraform plan/apply` + deploy no k3s |
| 3 | [InfraSGBD](https://github.com/Fiap-Mecanic-Ltda/InfraSGBD) | Terraform do RDS SQL Server | `terraform plan/apply` |
| 4 | [MechanicLtda](https://github.com/Fiap-Mecanic-Ltda/MechanicLtda) | Aplicação .NET 9 | build, testes e push das imagens no ECR |

## Como este stack conversa com os outros

```text
InfraKubernete  ──(remote state)──▶  vpc_id, db_subnet_ids, app_subnet_ids, alb_listener_arn
InfraSGBD       ──(remote state)──▶  rds_security_group_id, nome do parâmetro SSM
SSM Parameter Store ─────────────▶  connection string + chave JWT + chave do hash do CPF
                                     │  (todas lidas no apply)
                                     ▼
        ┌───────────────── API Gateway HTTP API ─────────────────┐
        │  /auth/*        → Lambda (na VPC)          → RDS       │
        │  /api/*, /health, /swagger → VPC Link → ALB → k3s      │
        │  rotas protegidas → Lambda authorizer (fora da VPC)    │
        └────────────────────────────────────────────────────────┘
```

- A função de autenticação roda **dentro da VPC**, nas mesmas subnets privadas do banco, com um
  security group próprio; a regra que libera a porta 1433 é criada **aqui**
  (`aws_vpc_security_group_ingress_rule` apontando para o SG do RDS), não no repositório 3 — assim
  não há dependência circular entre os stacks.
- O **authorizer** fica fora da VPC: só valida token, não faz I/O. Sem ENI, o cold start é menor —
  e ele entra no caminho de toda requisição protegida.
- O **VPC Link** cria ENIs nas sub-redes de aplicação (`app_subnet_ids`) e alcança o ALB interno
  publicado pelo repositório 2. O security group dos nós do k3s aceita a porta 8080 apenas do SG
  do ALB, então o backend deixa de ser acessível pela internet.
- Os segredos são lidos do **SSM no momento do `apply`** e injetados como variáveis de ambiente da
  função. Aquelas subnets não têm rota para a internet nem VPC endpoints, então uma chamada ao SSM
  em runtime ficaria pendurada até o timeout. As variáveis de ambiente da Lambda são cifradas em
  repouso com KMS. Rotacionar um segredo exige novo `apply`, não só atualizar o parâmetro.

## Estrutura

```text
Lambda/
├── src/MechanicLtda.Auth.Lambda/
│   ├── Function.cs              # handler das duas rotas de autenticação (payload v2)
│   ├── Authorizer.cs            # handler do Lambda authorizer do gateway
│   ├── TokenAuthorizer.cs       # regra do authorizer: token válido + role x rota
│   ├── UsuarioRepository.cs     # consulta AspNetUsers/AspNetRoles (SqlClient)
│   ├── ClienteRepository.cs     # consulta Clientes pelo índice cego do CPF
│   ├── CpfValidator.cs          # dígitos verificadores, igual ao CpfCnpjAttribute da API
│   ├── DocumentoHash.cs         # HMAC-SHA256 do CPF (contrato com a aplicação)
│   ├── JwtTokenService.cs       # tokens de usuário e de cliente
│   └── Models.cs                # contratos de entrada/saída e o enum de tipo
├── tests/MechanicLtda.Auth.Lambda.Tests/
│   ├── JwtTokenServiceTests.cs  # o token passa na mesma validação da API
│   ├── TokenDoClienteTests.cs   # claims e validade do token emitido por CPF
│   ├── TokenAuthorizerTests.cs  # permite/nega por token, issuer, expiração e rota
│   ├── CpfValidatorTests.cs     # CPFs válidos e inválidos
│   ├── DocumentoHashTests.cs    # vetor de contrato do hash com a aplicação
│   └── SenhaIdentityTests.cs    # compatibilidade com os hashes do Identity
├── infra/                       # Terraform: funções, API Gateway, VPC Link, IAM, SG, logs
│   ├── apigateway.tf            # API, rotas de autenticação, stage, throttling, access log
│   ├── cluster.tf               # VPC Link, integrações privadas, authorizer e rotas de /api
│   ├── lambda.tf                # função de autenticação (na VPC) e authorizer (fora dela)
│   └── data.tf                  # states dos outros stacks e segredos do SSM
├── scripts/build.sh             # publica e empacota em build/function.zip (um zip, dois handlers)
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
3. **apply** — **automático em push na `main`** (e manual via `workflow_dispatch`, também só a
   partir da `main`), no Environment `production`. Depois do apply roda o
   **smoke test** (`scripts/smoke-test.sh`) contra a URL publicada.

| Branch | O que roda |
|---|---|
| PR para `homologacao` ou `main` | build, testes e plan |
| push em `homologacao` | build, testes e plan — não há ambiente de homologação na AWS |
| push em `main` | build, testes, plan, **apply** e smoke test |

Se o Environment `production` tiver revisores obrigatórios, o apply espera a aprovação — com o
log do plan já visível no mesmo run.

### Smoke test

```bash
bash scripts/smoke-test.sh "$(terraform -chdir=infra output -raw api_base_url)"
```

Sem segredo nenhum, verifica: `/health` pelo VPC Link (com retry, porque o VPC Link recém-criado
demora a ficar pronto), `/auth/cpf` com CPF inválido (`400`) e com CPF sem cadastro (`401`, que
prova a ida ao RDS), rota protegida sem token (`401`) e com token inválido (`403`).

Com `SMOKE_CPF` — o CPF de um cliente **ativo** — verifica também o fluxo que o desafio pede para
demonstrar: token por CPF, `200` na rota das próprias ordens de serviço, `403` na de outro cliente
e `403` numa rota administrativa.

### Secrets necessários

| Nome | Tipo | Uso |
|---|---|---|
| `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` | Secret | Chave do `terraform-deployer` (também lê os states remotos dos repositórios 2 e 3) |
| `SMOKE_CPF` | Secret (opcional) | CPF de um cliente ativo, para o smoke test cobrir o fluxo completo com token |

O Environment `production` precisa existir para o job de `apply`.

## Ordem de subida

1. `terraform apply` no **InfraKubernete** — VPC, sub-redes (incluindo as de aplicação), ALB
   interno e o parâmetro SSM `cpf-hash-key`.
2. `terraform apply` no **InfraSGBD** — RDS e connection string no SSM.
3. Deploy da aplicação (repositório 4 publica a imagem, `deploy.yml` do repositório 2 aplica no
   cluster) — a coluna `Clientes.CpfCnpjHash` precisa existir antes de a autenticação por CPF
   encontrar alguém.
4. `apply` **aqui** — as funções e o gateway só sobem depois dos states acima, porque leem
   `alb_listener_arn`, `app_subnet_ids`, `rds_security_group_id` e os parâmetros do SSM. Um
   `plan` antes de o parâmetro `cpf-hash-key` existir falha na leitura do `data`.
5. Publique a URL do gateway: `terraform output api_base_url` alimenta
   `app_base_url_aprovacao` no repositório 2 (links de aprovação por e-mail) e o README da
   aplicação.
6. Validado o gateway, rode o apply do repositório 2 com `expose_nodeport_publicly = false`
   para fechar o acesso direto à porta 8080.

## Fluxo de trabalho no Git

A branch de trabalho é **`homologacao`**; nada é commitado direto na `main` — a `main` recebe
mudanças por Pull Request. O workflow roda build/testes/plan nas duas branches; o `apply` é sempre
manual.
