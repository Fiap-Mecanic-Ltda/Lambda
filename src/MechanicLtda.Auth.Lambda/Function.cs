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

    private static readonly UsuarioRepository Repositorio = new(ConnectionString);

    // Mesmo formato de hash gravado pela API (Identity v3, PBKDF2-HMAC-SHA256).
    private static readonly PasswordHasher<UsuarioAutenticavel> Hasher = new();

    public async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
    {
        try
        {
            var login = Desserializar(request);
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
        catch (Exception ex)
        {
            context.Logger.LogError($"Falha ao autenticar: {ex}");
            return Resposta(500, new ErroResponse { Mensagem = "Erro ao processar a autenticacao." });
        }
    }

    private static LoginRequest? Desserializar(APIGatewayHttpApiV2ProxyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Body))
            return null;

        var corpo = request.IsBase64Encoded
            ? Encoding.UTF8.GetString(Convert.FromBase64String(request.Body))
            : request.Body;

        try
        {
            return JsonSerializer.Deserialize<LoginRequest>(corpo, JsonOptions);
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
