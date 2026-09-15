using MechanicLtda.Auth.Lambda;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Xunit;

namespace MechanicLtda.Auth.Lambda.Tests;

/// <summary>
/// O authorizer é o que impede requisição sem token válido de chegar ao cluster.
/// Estes testes cobrem as decisões que o API Gateway toma antes do backend:
/// autenticação do token e filtro de role por rota.
/// </summary>
public class TokenAuthorizerTests
{
    private const string SecretKey  = "ChaveDeTesteMechanicLtda_Minimo32Caracteres!";
    private const string IssuerApi  = "MechanicLtda.API";
    private const string IssuerCpf  = "MechanicLtda.Auth.Cpf";
    private const string Audience   = "MechanicLtda.Clients";
    private const string RotaCliente = "GET /api/ordemservico/cliente/{clienteId}";
    private const string RotaAdmin   = "ANY /api/{proxy+}";

    private static TokenAuthorizer CriarAutorizador() =>
        new(SecretKey, [IssuerApi, IssuerCpf], Audience, [RotaCliente]);

    private static string GerarToken(
        string issuer,
        string[] roles,
        string? clienteId = null,
        int expiracaoMinutos = 30,
        string? chave = null,
        string? audience = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, clienteId ?? "42"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        if (clienteId is not null)
            claims.Add(new Claim("clienteId", clienteId));

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject  = new ClaimsIdentity(claims),
            Issuer   = issuer,
            Audience = audience ?? Audience,

            // NotBefore explícito no passado: sem ele o handler recusa emitir um
            // token com Expires anterior ao "agora", que é justamente o que o
            // teste de token expirado precisa produzir.
            NotBefore          = DateTime.UtcNow.AddHours(-1),
            Expires            = DateTime.UtcNow.AddMinutes(expiracaoMinutos),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(chave ?? SecretKey)),
                SecurityAlgorithms.HmacSha256Signature),
        };

        var handler = new JwtSecurityTokenHandler();

        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    [Fact]
    public void Autorizar_TokenDeClienteNaRotaDoCliente_DevePermitirEDevolverOContexto()
    {
        var token = GerarToken(IssuerCpf, ["Cliente"], clienteId: "42");

        var decisao = CriarAutorizador().Autorizar($"Bearer {token}", RotaCliente);

        Assert.True(decisao.Autorizado);
        Assert.Equal("Cliente", decisao.Role);
        Assert.Equal("42", decisao.ClienteId);
    }

    [Fact]
    public void Autorizar_TokenDeClienteEmRotaAdministrativa_DeveNegar()
    {
        var token = GerarToken(IssuerCpf, ["Cliente"], clienteId: "42");

        var decisao = CriarAutorizador().Autorizar($"Bearer {token}", RotaAdmin);

        Assert.False(decisao.Autorizado);
    }

    [Fact]
    public void Autorizar_TokenDeClienteSemClaimClienteId_DeveNegar()
    {
        var token = GerarToken(IssuerCpf, ["Cliente"]);

        var decisao = CriarAutorizador().Autorizar($"Bearer {token}", RotaCliente);

        Assert.False(decisao.Autorizado);
    }

    [Theory]
    [InlineData("Administrador")]
    [InlineData("Funcionario")]
    public void Autorizar_TokenAdministrativo_DevePermitirQualquerRota(string role)
    {
        var token = GerarToken(IssuerApi, [role]);

        var autorizador = CriarAutorizador();

        Assert.True(autorizador.Autorizar($"Bearer {token}", RotaAdmin).Autorizado);
        Assert.True(autorizador.Autorizar($"Bearer {token}", RotaCliente).Autorizado);
    }

    [Fact]
    public void Autorizar_TokenExpirado_DeveNegar()
    {
        var token = GerarToken(IssuerCpf, ["Cliente"], clienteId: "42", expiracaoMinutos: -1);

        Assert.False(CriarAutorizador().Autorizar($"Bearer {token}", RotaCliente).Autorizado);
    }

    [Fact]
    public void Autorizar_TokenAssinadoComOutraChave_DeveNegar()
    {
        var token = GerarToken(IssuerCpf, ["Cliente"], clienteId: "42",
            chave: "OutraChaveDeTeste_MechanicLtda_Minimo32Chars!");

        Assert.False(CriarAutorizador().Autorizar($"Bearer {token}", RotaCliente).Autorizado);
    }

    [Fact]
    public void Autorizar_TokenDeOutroEmissor_DeveNegar()
    {
        var token = GerarToken("emissor.desconhecido", ["Cliente"], clienteId: "42");

        Assert.False(CriarAutorizador().Autorizar($"Bearer {token}", RotaCliente).Autorizado);
    }

    [Fact]
    public void Autorizar_TokenDeOutraAudience_DeveNegar()
    {
        var token = GerarToken(IssuerCpf, ["Cliente"], clienteId: "42", audience: "outra-audiencia");

        Assert.False(CriarAutorizador().Autorizar($"Bearer {token}", RotaCliente).Autorizado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer")]
    [InlineData("Bearer ")]
    [InlineData("Basic dXNlcjpzZW5oYQ==")]
    [InlineData("nao-e-um-token")]
    public void Autorizar_SemBearerValido_DeveNegar(string? header)
    {
        Assert.False(CriarAutorizador().Autorizar(header, RotaCliente).Autorizado);
    }

    [Fact]
    public void Autorizar_TokenSemRole_DeveNegar()
    {
        var token = GerarToken(IssuerCpf, []);

        Assert.False(CriarAutorizador().Autorizar($"Bearer {token}", RotaCliente).Autorizado);
    }
}
