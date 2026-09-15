namespace MechanicLtda.Auth.Lambda;

/// <summary>
/// Validação de CPF por dígitos verificadores — o mesmo cálculo do
/// <c>CpfCnpjAttribute</c> da API. Roda antes de qualquer consulta ao banco:
/// documento malformado é erro do cliente (400), não credencial inválida (401),
/// e não deve custar uma ida ao RDS.
/// </summary>
public static class CpfValidator
{
    /// <summary>Devolve apenas os dígitos do documento (remove máscara e espaços).</summary>
    public static string Normalizar(string? documento)
    {
        if (string.IsNullOrWhiteSpace(documento))
            return string.Empty;

        return string.Concat(documento.Where(char.IsDigit));
    }

    /// <summary>
    /// Valida um CPF já normalizado ou com máscara. Rejeita tamanho diferente de
    /// 11 e sequências de dígitos repetidos (00000000000, 11111111111, ...), que
    /// passam na conta dos dígitos verificadores mas não são CPFs válidos.
    /// </summary>
    public static bool EhValido(string? documento)
    {
        var cpf = Normalizar(documento);

        if (cpf.Length != 11)
            return false;

        if (cpf.Distinct().Count() == 1)
            return false;

        var d1 = Digito(cpf, [10, 9, 8, 7, 6, 5, 4, 3, 2]);
        var d2 = Digito(cpf, [11, 10, 9, 8, 7, 6, 5, 4, 3, 2]);

        return cpf[9] - '0' == d1 && cpf[10] - '0' == d2;
    }

    private static int Digito(string cpf, int[] pesos)
    {
        var soma = pesos.Select((peso, indice) => (cpf[indice] - '0') * peso).Sum();
        var resto = soma % 11;

        return resto < 2 ? 0 : 11 - resto;
    }
}
