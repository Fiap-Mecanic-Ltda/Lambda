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

    // Montadas uma vez por container (o custo de ler as variaveis e abrir a
    // configuracao so e pago no cold start) e reaproveitadas entre invocacoes.
    private static readonly Lazy<DependenciasAutenticacao> Padrao = new(DependenciasAutenticacao.DoAmbiente);

    // Mesmo formato de hash gravado pela API (Identity v3, PBKDF2-HMAC-SHA256).
    private static readonly PasswordHasher<UsuarioAutenticavel> Hasher = new();

    private readonly DependenciasAutenticacao _dependencias;

    /// <summary>Usado pelo runtime do Lambda: le a configuracao das variaveis de ambiente.</summary>
    public Function() : this(Padrao.Value)
    {
    }

    /// <summary>Permite trocar banco e configuracao (testes).</summary>
    public Function(DependenciasAutenticacao dependencias) => _dependencias = dependencias;

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

    private async Task<APIGatewayHttpApiV2ProxyResponse> AutenticarPorSenhaAsync(
        APIGatewayHttpApiV2ProxyRequest request)
    {
        var login = Desserializar<LoginRequest>(request);
        if (login is null || string.IsNullOrWhiteSpace(login.Email) || string.IsNullOrWhiteSpace(login.Senha))
            return Resposta(400, new ErroResponse { Mensagem = "Informe e-mail e senha." });

        var usuario = await _dependencias.Usuarios.BuscarPorEmailAsync(login.Email!);

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
        return Resposta(200, _dependencias.TokenService.Gerar(usuario));
    }

    // ── POST /auth/cpf ───────────────────────────────────────────────────────

    private async Task<APIGatewayHttpApiV2ProxyResponse> AutenticarPorCpfAsync(
        APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
    {
        var cpfHashKey = _dependencias.CpfHashKey;
        if (string.IsNullOrWhiteSpace(cpfHashKey))
        {
            context.Logger.LogError("CPF_HASH_KEY nao configurada: autenticacao por CPF indisponivel.");
            return Resposta(503, new ErroResponse { Mensagem = "Autenticacao por CPF indisponivel." });
        }

        var corpo = Desserializar<CpfRequest>(request);

        // CPF malformado e erro de quem chamou, nao credencial invalida - e nao
        // custa uma ida ao banco.
        if (!CpfValidator.EhValido(corpo?.Cpf))
            return Resposta(400, new ErroResponse { Mensagem = "CPF invalido." });

        var hash    = DocumentoHash.Gerar(corpo!.Cpf, cpfHashKey);
        var cliente = await _dependencias.Clientes.BuscarPorHashAsync(hash);

        // Inexistente e inativo devolvem a mesma resposta: distinguir os dois
        // permitiria descobrir, um CPF por vez, quem e cliente da oficina.
        // No log vai so um prefixo do hash - nunca o CPF.
        if (cliente is null || !cliente.Ativo)
        {
            context.Logger.LogInformation(
                $"Autenticacao por CPF negada (hash {hash[..8]}..., encontrado={cliente is not null}).");

            return Resposta(401, new ErroResponse { Mensagem = "Nao foi possivel autenticar com o CPF informado." });
        }

        var token = _dependencias.TokenService.GerarParaCliente(cliente, _dependencias.IssuerCpf, _dependencias.ExpiracaoCpfMinutos);

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
}
