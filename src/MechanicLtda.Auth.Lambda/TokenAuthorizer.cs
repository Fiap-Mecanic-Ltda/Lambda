using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace MechanicLtda.Auth.Lambda;

/// <summary>Decisão do authorizer, com o contexto repassado ao backend.</summary>
public sealed record DecisaoAutorizacao(
    bool Autorizado,
    string? Motivo = null,
    string? Sub = null,
    string? Role = null,
    string? ClienteId = null);

/// <summary>
/// Regra do Lambda authorizer do API Gateway, separada do handler para ser
/// testável sem variáveis de ambiente.
///
/// Faz duas coisas, e só duas:
///
/// 1. **Autenticação** — assinatura HMAC-SHA256, <c>exp</c>, <c>aud</c> e
///    <c>iss</c> (dois emissores válidos: a API e a autenticação por CPF).
/// 2. **Filtro grosso de role por rota** — um token de cliente só passa nas
///    rotas do cliente; token administrativo passa em todas.
///
/// A posse do recurso (o <c>clienteId</c> da rota ser o do token) fica na API,
/// de propósito: a resposta do authorizer é cacheada por
/// (Authorization, routeKey), e uma decisão que dependesse do valor do path
/// seria reaproveitada para outro <c>clienteId</c> dentro da janela do cache.
/// </summary>
public sealed class TokenAuthorizer
{
    private const string RoleCliente = "Cliente";

    private static readonly string[] RolesAdministrativas = ["Administrador", "Funcionario"];

    private readonly TokenValidationParameters _parametros;
    private readonly string[] _rotasDoCliente;

    public TokenAuthorizer(string secretKey, IEnumerable<string> issuers, string audience, IEnumerable<string> rotasDoCliente)
    {
        _rotasDoCliente = rotasDoCliente
            .Select(rota => rota.Trim())
            .Where(rota => rota.Length > 0)
            .ToArray();

        _parametros = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuers             = issuers.Where(i => !string.IsNullOrWhiteSpace(i)).ToArray(),
            ValidAudience            = audience,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),

            // Mesma tolerância da API (AuthenticationExtension): zero. Token
            // expirado é token expirado nos dois lados.
            ClockSkew = TimeSpan.Zero,
        };
    }

    public DecisaoAutorizacao Autorizar(string? headerAuthorization, string? routeKey)
    {
        var token = ExtrairBearer(headerAuthorization);

        if (token is null)
            return new DecisaoAutorizacao(false, "sem token Bearer");

        ClaimsPrincipal principal;

        try
        {
            principal = new JwtSecurityTokenHandler().ValidateToken(token, _parametros, out _);
        }
        catch (SecurityTokenException ex)
        {
            return new DecisaoAutorizacao(false, $"token invalido: {ex.GetType().Name}");
        }

        var roles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
        var sub   = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                    ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        var clienteId = principal.FindFirst("clienteId")?.Value;

        if (roles.Intersect(RolesAdministrativas).Any())
            return new DecisaoAutorizacao(true, null, sub, string.Join(',', roles), clienteId);

        if (roles.Contains(RoleCliente))
        {
            if (!RotaPermitidaParaCliente(routeKey))
                return new DecisaoAutorizacao(false, $"rota fora do escopo do cliente: {routeKey}");

            if (string.IsNullOrWhiteSpace(clienteId))
                return new DecisaoAutorizacao(false, "token de cliente sem claim clienteId");

            return new DecisaoAutorizacao(true, null, sub, RoleCliente, clienteId);
        }

        return new DecisaoAutorizacao(false, "token sem role reconhecida");
    }

    private bool RotaPermitidaParaCliente(string? routeKey) =>
        !string.IsNullOrWhiteSpace(routeKey)
        && _rotasDoCliente.Any(rota => rota.Equals(routeKey, StringComparison.OrdinalIgnoreCase));

    private static string? ExtrairBearer(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
            return null;

        const string prefixo = "Bearer ";

        if (!header.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase))
            return null;

        var token = header[prefixo.Length..].Trim();

        return token.Length == 0 ? null : token;
    }
}
