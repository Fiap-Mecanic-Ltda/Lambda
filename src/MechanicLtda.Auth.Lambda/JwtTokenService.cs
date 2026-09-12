using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace MechanicLtda.Auth.Lambda;

/// <summary>
/// Gera exatamente o mesmo token que AuthAppService.GerarTokenAsync, na API:
/// mesmas claims, mesmo issuer/audience e HMAC-SHA256 sobre a chave em UTF-8.
/// Se divergir, a API rejeita o token emitido aqui.
/// </summary>
public sealed class JwtTokenService
{
    private readonly string _secretKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int    _expiracaoMinutos;

    public JwtTokenService(string secretKey, string issuer, string audience, int expiracaoMinutos)
    {
        _secretKey        = secretKey;
        _issuer           = issuer;
        _audience         = audience;
        _expiracaoMinutos = expiracaoMinutos;
    }

    public TokenResponse Gerar(UsuarioAutenticavel usuario)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,   usuario.Id),
            new(JwtRegisteredClaimNames.Email, usuario.Email),
            new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
            new("userName", usuario.UserName),
            new("tipo",     TipoUsuario.Nome(usuario.Tipo)),
        };

        claims.AddRange(usuario.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var expiracao = DateTime.UtcNow.AddMinutes(_expiracaoMinutos);

        var descriptor = new SecurityTokenDescriptor
        {
            Subject            = new ClaimsIdentity(claims),
            Expires            = expiracao,
            Issuer             = _issuer,
            Audience           = _audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey)),
                SecurityAlgorithms.HmacSha256Signature),
        };

        var handler = new JwtSecurityTokenHandler();
        var token   = handler.CreateToken(descriptor);

        return new TokenResponse
        {
            Token     = handler.WriteToken(token),
            Expiracao = expiracao,
        };
    }
}
