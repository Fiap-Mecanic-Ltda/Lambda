using Microsoft.Data.SqlClient;

namespace MechanicLtda.Auth.Lambda;

/// <summary>
/// Consulta a tabela <c>Clientes</c> pelo índice cego do CPF/CNPJ. Somente
/// leitura: o schema é responsabilidade das migrations da aplicação, e a função
/// nunca escreve nada.
/// </summary>
public sealed class ClienteRepository
{
    // Só as colunas necessárias para autenticar e montar o token. CpfCnpj (o
    // valor cifrado) não é lido: a função não tem a chave de criptografia nem
    // precisa dela.
    private const string SqlPorHash = """
        SELECT TOP 1 c.Id, c.Nome, c.Ativo
        FROM Clientes c
        WHERE c.CpfCnpjHash = @hash
        """;

    private readonly string _connectionString;

    public ClienteRepository(string connectionString) => _connectionString = connectionString;

    public async Task<ClienteAutenticavel?> BuscarPorHashAsync(string hash, CancellationToken ct = default)
    {
        await using var conexao = new SqlConnection(_connectionString);
        await conexao.OpenAsync(ct);

        await using var comando = new SqlCommand(SqlPorHash, conexao);
        comando.Parameters.AddWithValue("@hash", hash);

        await using var leitor = await comando.ExecuteReaderAsync(ct);

        if (!await leitor.ReadAsync(ct))
            return null;

        return new ClienteAutenticavel(
            Id: leitor.GetInt32(0),
            Nome: leitor.IsDBNull(1) ? string.Empty : leitor.GetString(1),
            Ativo: leitor.GetBoolean(2));
    }
}
