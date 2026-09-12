using Xunit;
using MechanicLtda.Auth.Lambda;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace MechanicLtda.Auth.Lambda.Tests;

/// <summary>
/// O token emitido pela funcao precisa passar na mesma validacao que a API faz
/// (AuthenticationExtension: issuer, audience, assinatura e lifetime, com
/// ClockSkew zero). Se estes testes quebrarem, a API vai responder 401 para
/// tokens gerados aqui.
/// </summary>
public class JwtTokenServiceTests
{
    private const string SecretKey = "ChaveDeTesteMechanicLtda_Minimo32Caracteres!";
    private const string Issuer    = "MechanicLtda.API";
    private const string Audience  = "MechanicLtda.Clients";

    private static readonly UsuarioAutenticavel Usuario = new(
        Id: "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
        UserName: "maria.souza",
        Email: "maria@oficina.com",
        PasswordHash: "irrelevante-para-o-token",
        Tipo: 2,
        Ativo: true,
        Roles: new[] { "Funcionario" });

    private static TokenResponse Gerar() =>
        new JwtTokenService(SecretKey, Issuer, Audience, 60).Gerar(Usuario);

    private static ClaimsPrincipal Validar(string token)
    {
        var parametros = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = Issuer,
            ValidAudience            = Audience,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey)),
            ClockSkew                = TimeSpan.Zero,
        };

        return new JwtSecurityTokenHandler().ValidateToken(token, parametros, out _);
    }

    [Fact]
    public void Token_e_aceito_pelos_mesmos_parametros_de_validacao_da_API()
    {
        var principal = Validar(Gerar().Token);

        Assert.Equal(Usuario.Id, principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                                 ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
    }

    [Fact]
    public void Token_carrega_as_claims_que_a_aplicacao_consome()
    {
        var principal = Validar(Gerar().Token);

        // O JwtSecurityTokenHandler traduz as claims do JWT para os tipos
        // longos do WS-Federation na validacao (email -> ClaimTypes.Email,
        // sub -> NameIdentifier). O JwtBearer da API usa o mesmo mapeamento.
        Assert.Equal(Usuario.Email, principal.FindFirst(ClaimTypes.Email)?.Value
                                    ?? principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value);
        Assert.Equal(Usuario.UserName, principal.FindFirst("userName")?.Value);
        Assert.Equal("Funcionario", principal.FindFirst("tipo")?.Value);
        Assert.True(principal.IsInRole("Funcionario"));
        Assert.NotNull(principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value);
    }

    [Fact]
    public void Token_assinado_com_outra_chave_e_rejeitado()
    {
        var token = new JwtTokenService("OutraChaveCompletamenteDiferente_32Chars!", Issuer, Audience, 60)
            .Gerar(Usuario).Token;

        Assert.Throws<SecurityTokenSignatureKeyNotFoundException>(() => Validar(token));
    }

    [Fact]
    public void Expiracao_respeita_a_janela_configurada()
    {
        var resposta = Gerar();

        Assert.InRange(resposta.Expiracao, DateTime.UtcNow.AddMinutes(59), DateTime.UtcNow.AddMinutes(61));
    }

    [Theory]
    [InlineData(1, "Administrador")]
    [InlineData(2, "Funcionario")]
    [InlineData(3, "Cliente")]
    public void Tipo_do_usuario_vira_o_mesmo_nome_do_enum_da_aplicacao(int tipo, string esperado)
    {
        Assert.Equal(esperado, TipoUsuario.Nome(tipo));
    }
}
