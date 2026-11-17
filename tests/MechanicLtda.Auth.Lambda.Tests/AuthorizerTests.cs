using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Amazon.Lambda.TestUtilities;
using MechanicLtda.Auth.Lambda;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace MechanicLtda.Auth.Lambda.Tests;

/// <summary>Handler do Lambda authorizer: leitura do header e montagem da resposta simples.</summary>
public class AuthorizerTests
{
    private const string SecretKey   = "ChaveDeTesteMechanicLtda_Minimo32Caracteres!";
    private const string RotaCliente = "GET /api/ordemservico/cliente/{clienteId}";

    private readonly TestLambdaContext _contexto = new();

    private static Authorizer Criar() => new(new TokenAuthorizer(
        SecretKey, ["MechanicLtda.API", "MechanicLtda.Auth.Cpf"], "MechanicLtda.Clients", [RotaCliente]));

    private static string Token(string role, string? clienteId = null)
    {
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, "7"), new(ClaimTypes.Role, role) };
        if (clienteId is not null)
            claims.Add(new Claim("clienteId", clienteId));

        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = "MechanicLtda.Auth.Cpf",
            Audience = "MechanicLtda.Clients",
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey)), SecurityAlgorithms.HmacSha256Signature),
        }));
    }

    [Fact]
    public void Token_valido_autoriza_e_devolve_o_contexto()
    {
        var resposta = Criar().FunctionHandler(new AuthorizerRequest
        {
            RouteKey = RotaCliente,
            // O header chega em minúsculas, mas a busca não depende disso.
            Headers = new() { ["X-Outro"] = "1", ["Authorization"] = $"Bearer {Token("Cliente", "42")}" },
        }, _contexto);

        Assert.True(resposta.IsAuthorized);
        Assert.Equal("7", resposta.Context["sub"]);
        Assert.Equal("Cliente", resposta.Context["role"]);
        Assert.Equal("42", resposta.Context["clienteId"]);
    }

    [Fact]
    public void Sem_headers_ou_sem_token_nega_e_registra_o_motivo()
    {
        var semHeaders = Criar().FunctionHandler(new AuthorizerRequest { RouteKey = RotaCliente }, _contexto);
        var semToken = Criar().FunctionHandler(new AuthorizerRequest { RouteKey = RotaCliente, Headers = new() { ["host"] = "x" } }, _contexto);

        Assert.False(semHeaders.IsAuthorized);
        Assert.False(semToken.IsAuthorized);
        Assert.Empty(semHeaders.Context);
        Assert.Contains("Acesso negado", ((TestLambdaLogger)_contexto.Logger).Buffer.ToString());
    }

    [Fact]
    public void Cliente_em_rota_administrativa_e_negado()
    {
        var resposta = Criar().FunctionHandler(new AuthorizerRequest
        {
            RouteKey = "ANY /api/{proxy+}",
            Headers = new() { ["authorization"] = $"Bearer {Token("Cliente", "42")}" },
        }, _contexto);

        Assert.False(resposta.IsAuthorized);
    }
}
