using System.Security.Cryptography;
using System.Text;

namespace MechanicLtda.Auth.Lambda;

/// <summary>
/// Índice cego do CPF/CNPJ: HMAC-SHA256 dos dígitos do documento, em hexadecimal
/// minúsculo. É o mesmo algoritmo do <c>DocumentoHashService</c> da aplicação —
/// ela grava <c>Clientes.CpfCnpjHash</c> e esta função consulta por ele.
///
/// Existe porque a coluna <c>CpfCnpj</c> é cifrada com IV aleatório: o mesmo CPF
/// gera textos cifrados diferentes, então não há como fazer
/// <c>WHERE CpfCnpj = @cpf</c>. Com o hash, a função encontra o cliente sem
/// nunca conhecer a chave de criptografia.
///
/// Se este cálculo divergir do da aplicação, nenhum cliente é encontrado. O
/// vetor de teste em <c>DocumentoHashTests</c> é o mesmo do teste da API.
/// </summary>
public static class DocumentoHash
{
    public static string Gerar(string? documento, string chave)
    {
        var digitos = CpfValidator.Normalizar(documento);

        if (digitos.Length == 0)
            throw new ArgumentException("Documento sem digitos para gerar o hash.", nameof(documento));

        if (string.IsNullOrWhiteSpace(chave))
            throw new ArgumentException("Chave do hash nao configurada.", nameof(chave));

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(chave));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(digitos));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
