using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Microsoft.AspNetCore.Identity;
using System.Text;
using System.Text.Json;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace MechanicLtda.Auth.Lambda;

public sealed class Function
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Reaproveitados entre invocacoes no mesmo container (o custo de ler as
    // variaveis e montar o hasher so e pago no cold start).
    private static readonly string ConnectionString = LerObrigatoria("DB_CONNECTION_STRING");
    private static readonly string JwtSecretKey     = LerObrigatoria("JWT_SECRET_KEY");

    private static readonly JwtTokenService TokenService = new(
        JwtSecretKey,
        Environment.GetEnvironmentVariable("JWT_ISSUER")   ?? "MechanicLtda.API",
        Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "MechanicLtda.Clients",
        int.TryParse(Environment.GetEnvironmentVariable("JWT_EXPIRACAO_MINUTOS"), out var minutos) ? minutos : 60);

    private static readonly UsuarioRepository Repositorio        = new(ConnectionString);
    private static readonly ClienteRepository ClienteRepositorio = new(ConnectionString);

    // Mesmo formato de hash gravado pela API (Identity v3, PBKDF2-HMAC-SHA256).
    private static readonly PasswordHasher<UsuarioAutenticavel> Hasher = new();

    // Autenticacao por CPF. A chave do indice cego nao entra em LerObrigatoria
    // de proposito: se faltar, so a rota /auth/cpf fica indisponivel (com log
    // explicito) - o login por e-mail e senha continua de pe.
    private static readonly string? CpfHashKey = Environment.GetEnvironmentVariable("CPF_HASH_KEY");

    private static readonly string IssuerCpf =
        Environment.GetEnvironmentVariable("JWT_ISSUER_CPF") ?? "MechanicLtda.Auth.Cpf";

    // Janela curta: o token de cliente e obtido so com o CPF, entao vale menos
    // tempo que o token de funcionario (que exige senha).
    private static readonly int ExpiracaoCpfMinutos =
        int.TryParse(Environment.GetEnvironmentVariable("JWT_CPF_EXPIRACAO_MINUTOS"), out var m) ? m : 30;

    public async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
    {
        try
        {
            // Uma funcao, duas rotas: o API Gateway informa qual foi acionada.
            // Compartilhar o container evita um segundo cold start e mantem o
            // mesmo pacote e os mesmos segredos para os dois fluxos.
            var rota = request.RouteKey ?? request.RequestContext?.Http?.Path ?? string.Empty;

            return rota.Contains("/auth/cpf", StringComparison.OrdinalIgnoreCase)
                ? await AutenticarPorCpfAsync(request, context)
                : await AutenticarPorSenhaAsync(request);
        }
        catch (Exception ex)
        {
            context.Logger.LogError($"Falha ao autenticar: {ex}");
            return Resposta(500, new ErroResponse { Mensagem = "Erro ao processar a autenticacao." });
        }
    }

    // ── POST /auth/login ─────────────────────────────────────────────────────

    private static async Task<APIGatewayHttpApiV2ProxyResponse> AutenticarPorSenhaAsync(
        APIGatewayHttpApiV2ProxyRequest request)
    {
        var login = Desserializar<LoginRequest>(request);
        if (login is null || string.IsNullOrWhiteSpace(login.Email) || string.IsNullOrWhiteSpace(login.Senha))
            return Resposta(400, new ErroResponse { Mensagem = "Informe e-mail e senha." });

        var usuario = await Repositorio.BuscarPorEmailAsync(login.Email!);

        // Mesma mensagem generica da API para os tres casos (inexistente,
        // inativo, senha errada): detalhar aqui entregaria de graca quais
        // e-mails existem na base.
        if (usuario is null || !usuario.Ativo || string.IsNullOrEmpty(usuario.PasswordHash))
            return Resposta(401, new ErroResponse { Mensagem = "Credenciais invalidas." });

        var resultado = Hasher.VerifyHashedPassword(usuario, usuario.PasswordHash, login.Senha!);
        if (resultado == PasswordVerificationResult.Failed)
            return Resposta(401, new ErroResponse { Mensagem = "Credenciais invalidas." });

        // SuccessRehashNeeded e sucesso: quem regrava o hash com os
        // parametros novos e a API, no proximo login por la.
        return Resposta(200, TokenService.Gerar(usuario));
    }

    // ── POST /auth/cpf ───────────────────────────────────────────────────────

    private static async Task<APIGatewayHttpApiV2ProxyResponse> AutenticarPorCpfAsync(
        APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
    {
        if (string.IsNullOrWhiteSpace(CpfHashKey))
        {
            context.Logger.LogError("CPF_HASH_KEY nao configurada: autenticacao por CPF indisponivel.");
            return Resposta(503, new ErroResponse { Mensagem = "Autenticacao por CPF indisponivel." });
        }

        var corpo = Desserializar<CpfRequest>(request);

        // CPF malformado e erro de quem chamou, nao credencial invalida - e nao
        // custa uma ida ao banco.
        if (!CpfValidator.EhValido(corpo?.Cpf))
            return Resposta(400, new ErroResponse { Mensagem = "CPF invalido." });

        var hash    = DocumentoHash.Gerar(corpo!.Cpf, CpfHashKey!);
        var cliente = await ClienteRepositorio.BuscarPorHashAsync(hash);

        // Inexistente e inativo devolvem a mesma resposta: distinguir os dois
        // permitiria descobrir, um CPF por vez, quem e cliente da oficina.
        // No log vai so um prefixo do hash - nunca o CPF.
        if (cliente is null || !cliente.Ativo)
        {
            context.Logger.LogInformation(
                $"Autenticacao por CPF negada (hash {hash[..8]}..., encontrado={cliente is not null}).");

            return Resposta(401, new ErroResponse { Mensagem = "Nao foi possivel autenticar com o CPF informado." });
        }

        var token = TokenService.GerarParaCliente(cliente, IssuerCpf, ExpiracaoCpfMinutos);

        return Resposta(200, new TokenClienteResponse
        {
            Token     = token.Token,
            Expiracao = token.Expiracao,
            Cliente   = new ClienteResumo { Id = cliente.Id, Nome = cliente.Nome },
        });
    }

    // ── infraestrutura ───────────────────────────────────────────────────────

    private static T? Desserializar<T>(APIGatewayHttpApiV2ProxyRequest request) where T : class
    {
        if (string.IsNullOrWhiteSpace(request.Body))
            return null;

        var corpo = request.IsBase64Encoded
            ? Encoding.UTF8.GetString(Convert.FromBase64String(request.Body))
            : request.Body;

        try
        {
            return JsonSerializer.Deserialize<T>(corpo, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static APIGatewayHttpApiV2ProxyResponse Resposta(int status, object corpo) => new()
    {
        StatusCode = status,
        Body       = JsonSerializer.Serialize(corpo, JsonOptions),
        Headers    = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
    };

    private static string LerObrigatoria(string nome) =>
        Environment.GetEnvironmentVariable(nome)
        ?? throw new InvalidOperationException($"Variavel de ambiente '{nome}' nao configurada na funcao.");
}
