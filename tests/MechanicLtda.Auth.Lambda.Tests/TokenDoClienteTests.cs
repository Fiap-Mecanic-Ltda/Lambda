using MechanicLtda.Auth.Lambda;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Xunit;

namespace MechanicLtda.Auth.Lambda.Tests;

/// <summary>
/// O token da autenticação por CPF precisa passar na validação da API
/// (AuthenticationExtension: dois issuers aceitos, audience, assinatura e
/// lifetime com ClockSkew zero) e carregar o claim clienteId, que é o que
/// permite à API conferir a posse do recurso.
/// </summary>
public class TokenDoClienteTests
{
    private const string SecretKey = "ChaveDeTesteMechanicLtda_Minimo32Caracteres!";
    private const string IssuerApi = "MechanicLtda.API";
    private const string IssuerCpf = "MechanicLtda.Auth.Cpf";
    private const string Audience  = "MechanicLtda.Clients";

    private static readonly ClienteAutenticavel Cliente = new(Id: 42, Nome: "Fernanda Lima", Ativo: true);

    private static TokenResponse Gerar(int expiracaoMinutos = 30) =>
        new JwtTokenService(SecretKey, IssuerApi, Audience, 60)
            .GerarParaCliente(Cliente, IssuerCpf, expiracaoMinutos);

    /// <summary>Mesmos parâmetros que a API usa para validar o token.</summary>
    private static ClaimsPrincipal ValidarComoAApi(string token)
    {
        var parametros = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuers             = [IssuerApi, IssuerCpf],
            ValidAudience            = Audience,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey)),
            ClockSkew                = TimeSpan.Zero,
        };

        return new JwtSecurityTokenHandler().ValidateToken(token, parametros, out _);
    }

    [Fact]
    public void GerarParaCliente_DeveSerAceitoPelaValidacaoDaApi()
    {
        var principal = ValidarComoAApi(Gerar().Token);

        Assert.True(principal.Identity?.IsAuthenticated);
    }

    [Fact]
    public void GerarParaCliente_DeveTerRoleClienteEClaimClienteId()
    {
        var principal = ValidarComoAApi(Gerar().Token);

        Assert.True(principal.IsInRole("Cliente"));
        Assert.Equal("42", principal.FindFirst("clienteId")?.Value);
        Assert.Equal("Cliente", principal.FindFirst("tipo")?.Value);
    }

    [Fact]
    public void GerarParaCliente_DeveUsarOIdDoClienteComoSubject()
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(Gerar().Token);

        Assert.Equal("42", token.Subject);
        Assert.Equal(IssuerCpf, token.Issuer);
    }

    [Fact]
    public void GerarParaCliente_NaoDeveCarregarOCpfNemDadosDesnecessarios()
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(Gerar().Token);

        Assert.DoesNotContain(token.Claims, c => c.Type.Contains("cpf", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(token.Claims, c => c.Value.Contains("Fernanda", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GerarParaCliente_DeveRespeitarAExpiracaoInformada()
    {
        var resposta = Gerar(expiracaoMinutos: 30);

        var minutos = (resposta.Expiracao - DateTime.UtcNow).TotalMinutes;

        Assert.InRange(minutos, 29, 30.5);
    }

    // A recusa de token expirado é coberta em TokenAuthorizerTests, onde o token
    // é montado no próprio teste: o serviço não emite token já vencido (o
    // handler recusa Expires anterior ao NotBefore), e é isso que se quer.
}
