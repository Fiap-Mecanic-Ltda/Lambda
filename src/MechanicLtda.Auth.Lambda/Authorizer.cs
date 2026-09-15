using Amazon.Lambda.Core;
using System.Text.Json.Serialization;

namespace MechanicLtda.Auth.Lambda;

/// <summary>
/// Evento do Lambda authorizer do HTTP API (payload 2.0). Só os campos usados
/// na decisão — o evento real tem bem mais, e o serializador ignora o resto.
/// Os DTOs são declarados aqui, e não importados do pacote de eventos, para a
/// função não depender do nome exato do tipo entre versões do SDK.
/// </summary>
public sealed class AuthorizerRequest
{
    /// <summary>Ex.: <c>GET /api/ordemservico/cliente/{clienteId}</c>.</summary>
    [JsonPropertyName("routeKey")]
    public string? RouteKey { get; set; }

    /// <summary>O API Gateway entrega os nomes de header em minúsculas.</summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }
}

/// <summary>
/// Resposta simples (<c>enableSimpleResponses</c>): autoriza ou não, e devolve
/// o contexto que a integração repassa ao backend como header.
/// </summary>
public sealed class AuthorizerResponse
{
    [JsonPropertyName("isAuthorized")]
    public bool IsAuthorized { get; set; }

    [JsonPropertyName("context")]
    public Dictionary<string, string> Context { get; set; } = new();
}

/// <summary>
/// Handler do authorizer. Roda fora da VPC: só valida token, não faz I/O — assim
/// o cold start não paga a criação de ENI e a latência fica no menor patamar
/// possível, já que ele entra no caminho de toda requisição protegida.
/// </summary>
public sealed class Authorizer
{
    private static readonly TokenAuthorizer Autorizador = new(
        secretKey: Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
                   ?? throw new InvalidOperationException("Variavel de ambiente 'JWT_SECRET_KEY' nao configurada na funcao."),
        issuers:
        [
            Environment.GetEnvironmentVariable("JWT_ISSUER")     ?? "MechanicLtda.API",
            Environment.GetEnvironmentVariable("JWT_ISSUER_CPF") ?? "MechanicLtda.Auth.Cpf",
        ],
        audience: Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "MechanicLtda.Clients",
        rotasDoCliente: (Environment.GetEnvironmentVariable("ROTAS_DO_CLIENTE")
                         ?? "GET /api/ordemservico/cliente/{clienteId}").Split(';'));

    public AuthorizerResponse FunctionHandler(AuthorizerRequest request, ILambdaContext context)
    {
        var header = ObterHeader(request, "authorization");
        var decisao = Autorizador.Autorizar(header, request.RouteKey);

        if (!decisao.Autorizado)
        {
            // Sem o token no log: o motivo basta para diagnosticar 401 e 403 no
            // CloudWatch sem guardar credencial em texto.
            context.Logger.LogInformation($"Acesso negado em '{request.RouteKey}': {decisao.Motivo}.");

            return new AuthorizerResponse { IsAuthorized = false };
        }

        return new AuthorizerResponse
        {
            IsAuthorized = true,
            Context = new Dictionary<string, string>
            {
                ["sub"]       = decisao.Sub ?? string.Empty,
                ["role"]      = decisao.Role ?? string.Empty,
                ["clienteId"] = decisao.ClienteId ?? string.Empty,
            },
        };
    }

    private static string? ObterHeader(AuthorizerRequest request, string nome)
    {
        if (request.Headers is null)
            return null;

        foreach (var (chave, valor) in request.Headers)
        {
            if (string.Equals(chave, nome, StringComparison.OrdinalIgnoreCase))
                return valor;
        }

        return null;
    }
}
