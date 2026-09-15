using MechanicLtda.Auth.Lambda;
using Xunit;

namespace MechanicLtda.Auth.Lambda.Tests;

/// <summary>
/// Mesmo cálculo de dígitos verificadores do CpfCnpjAttribute da API. Se a
/// função aceitar um CPF que a API recusa (ou o contrário), o cliente encontra
/// comportamentos diferentes dependendo de por onde entra.
/// </summary>
public class CpfValidatorTests
{
    [Theory]
    [InlineData("529.982.247-25")]
    [InlineData("52998224725")]
    [InlineData("111.444.777-35")]
    [InlineData("123.456.789-09")]
    [InlineData("987.654.321-00")]
    public void EhValido_ComCpfValido_DeveAceitar(string cpf)
    {
        Assert.True(CpfValidator.EhValido(cpf));
    }

    [Theory]
    [InlineData("123.456.789-01")]  // dígitos verificadores errados (o correto é -09)
    [InlineData("234.567.890-12")]
    [InlineData("345.678.901-23")]
    [InlineData("529.982.247-26")]  // último dígito trocado
    [InlineData("111.111.111-11")]  // sequência repetida
    [InlineData("000.000.000-00")]
    [InlineData("5299822472")]      // 10 dígitos
    [InlineData("529982247251")]    // 12 dígitos
    [InlineData("abc.def.ghi-jk")]
    [InlineData("")]
    [InlineData(null)]
    public void EhValido_ComCpfInvalido_DeveRecusar(string? cpf)
    {
        Assert.False(CpfValidator.EhValido(cpf));
    }

    [Theory]
    [InlineData("529.982.247-25", "52998224725")]
    [InlineData(" 529 982 247 25 ", "52998224725")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Normalizar_DeveManterApenasOsDigitos(string? entrada, string esperado)
    {
        Assert.Equal(esperado, CpfValidator.Normalizar(entrada));
    }
}
