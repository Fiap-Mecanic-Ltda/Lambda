using MechanicLtda.Auth.Lambda;
using Xunit;

namespace MechanicLtda.Auth.Lambda.Tests;

/// <summary>
/// O índice cego é o contrato entre a aplicação e esta função: a API grava
/// <c>Clientes.CpfCnpjHash</c> e a função consulta por ele. Se os dois cálculos
/// divergirem, nenhum cliente é encontrado e a autenticação por CPF passa a
/// responder 401 para todo mundo.
/// </summary>
public class DocumentoHashTests
{
    private const string Chave = "chave-de-teste-do-hash-de-cpf-1234567890";

    /// <summary>
    /// Vetor idêntico ao de DocumentoHashServiceTests, no repositório da
    /// aplicação. Os dois testes precisam produzir este mesmo valor.
    /// </summary>
    private const string HashEsperadoDoCpf =
        "f865a3cc9a1cab6bba801ce82a120b4ed304a74dcfbdd31b8ba48c0cc6cc2dd8";

    [Fact]
    public void Gerar_DeveHonrarOVetorDeContratoComAAplicacao()
    {
        Assert.Equal(HashEsperadoDoCpf, DocumentoHash.Gerar("529.982.247-25", Chave));
    }

    [Fact]
    public void Gerar_ComEeSemMascara_DeveProduzirOMesmoHash()
    {
        Assert.Equal(
            DocumentoHash.Gerar("52998224725", Chave),
            DocumentoHash.Gerar("529.982.247-25", Chave));
    }

    [Fact]
    public void Gerar_ParaDocumentosDiferentes_DeveProduzirHashesDiferentes()
    {
        Assert.NotEqual(
            DocumentoHash.Gerar("52998224725", Chave),
            DocumentoHash.Gerar("11144477735", Chave));
    }

    [Fact]
    public void Gerar_ComChavesDiferentes_DeveProduzirHashesDiferentes()
    {
        Assert.NotEqual(
            DocumentoHash.Gerar("52998224725", Chave),
            DocumentoHash.Gerar("52998224725", "outra-chave-de-teste-do-hash-0987654321"));
    }

    [Fact]
    public void Gerar_DeveSer64CaracteresHexadecimaisMinusculos()
    {
        var hash = DocumentoHash.Gerar("52998224725", Chave);

        Assert.Equal(64, hash.Length);
        Assert.All(hash, caractere => Assert.Contains(caractere, "0123456789abcdef"));
    }

    [Fact]
    public void Gerar_SemDigitos_DeveLancarArgumentException()
    {
        Assert.Throws<ArgumentException>(() => DocumentoHash.Gerar("sem digitos", Chave));
    }

    [Fact]
    public void Gerar_SemChave_DeveLancarArgumentException()
    {
        Assert.Throws<ArgumentException>(() => DocumentoHash.Gerar("52998224725", ""));
    }
}
