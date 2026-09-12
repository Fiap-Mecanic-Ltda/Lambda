using Xunit;
using MechanicLtda.Auth.Lambda;
using Microsoft.AspNetCore.Identity;

namespace MechanicLtda.Auth.Lambda.Tests;

/// <summary>
/// A funcao valida as senhas ja gravadas em AspNetUsers pela API, entao precisa
/// usar o mesmo PasswordHasher do ASP.NET Core Identity - inclusive aceitando
/// hashes no formato v2, que devolvem SuccessRehashNeeded.
/// </summary>
public class SenhaIdentityTests
{
    private static readonly UsuarioAutenticavel Usuario = new(
        "id", "usuario", "usuario@oficina.com", string.Empty, 3, true, Array.Empty<string>());

    [Fact]
    public void Hash_gerado_pelo_Identity_e_verificado_com_sucesso()
    {
        var hasher = new PasswordHasher<UsuarioAutenticavel>();
        var hash   = hasher.HashPassword(Usuario, "Senha@123");

        Assert.Equal(PasswordVerificationResult.Success,
                     hasher.VerifyHashedPassword(Usuario, hash, "Senha@123"));
    }

    [Fact]
    public void Senha_errada_falha()
    {
        var hasher = new PasswordHasher<UsuarioAutenticavel>();
        var hash   = hasher.HashPassword(Usuario, "Senha@123");

        Assert.Equal(PasswordVerificationResult.Failed,
                     hasher.VerifyHashedPassword(Usuario, hash, "senha-errada"));
    }

    [Fact]
    public void Hash_no_formato_v2_e_aceito_com_rehash_pendente()
    {
        var hasherV2 = new PasswordHasher<UsuarioAutenticavel>(
            Microsoft.Extensions.Options.Options.Create(new PasswordHasherOptions
            {
                CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2,
            }));

        var hash = hasherV2.HashPassword(Usuario, "Senha@123");

        // O hasher padrao (v3) reconhece o hash antigo e pede rehash - a funcao
        // trata isso como sucesso, igual ao SignInManager da API.
        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded,
                     new PasswordHasher<UsuarioAutenticavel>().VerifyHashedPassword(Usuario, hash, "Senha@123"));
    }
}
