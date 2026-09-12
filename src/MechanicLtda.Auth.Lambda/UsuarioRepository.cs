using Microsoft.Data.SqlClient;

namespace MechanicLtda.Auth.Lambda;

/// <summary>
/// Le direto das tabelas do ASP.NET Core Identity (AspNetUsers/AspNetRoles/
/// AspNetUserRoles) criadas pelas migrations da aplicacao. A funcao nao aplica
/// migration nenhuma: o schema e sempre de responsabilidade da API.
/// </summary>
public sealed class UsuarioRepository
{
    private const string SqlUsuario = """
        SELECT TOP 1 u.Id, u.UserName, u.Email, u.PasswordHash, u.Tipo, u.Ativo
        FROM AspNetUsers u
        WHERE u.NormalizedEmail = @email
        """;

    private const string SqlRoles = """
        SELECT r.Name
        FROM AspNetUserRoles ur
        INNER JOIN AspNetRoles r ON r.Id = ur.RoleId
        WHERE ur.UserId = @userId
        """;

    private readonly string _connectionString;

    public UsuarioRepository(string connectionString) => _connectionString = connectionString;

    public async Task<UsuarioAutenticavel?> BuscarPorEmailAsync(string email, CancellationToken ct = default)
    {
        await using var conexao = new SqlConnection(_connectionString);
        await conexao.OpenAsync(ct);

        string id, userName, emailBanco, passwordHash;
        int tipo;
        bool ativo;

        await using (var comando = new SqlCommand(SqlUsuario, conexao))
        {
            // O Identity guarda o e-mail normalizado em maiusculas (invariant
            // culture); comparar com a coluna crua erraria em bases com
            // collation case-sensitive.
            comando.Parameters.AddWithValue("@email", email.ToUpperInvariant());

            await using var leitor = await comando.ExecuteReaderAsync(ct);
            if (!await leitor.ReadAsync(ct))
                return null;

            id           = leitor.GetString(0);
            userName     = leitor.IsDBNull(1) ? string.Empty : leitor.GetString(1);
            emailBanco   = leitor.IsDBNull(2) ? string.Empty : leitor.GetString(2);
            passwordHash = leitor.IsDBNull(3) ? string.Empty : leitor.GetString(3);
            tipo         = leitor.GetInt32(4);
            ativo        = leitor.GetBoolean(5);
        }

        var roles = new List<string>();
        await using (var comandoRoles = new SqlCommand(SqlRoles, conexao))
        {
            comandoRoles.Parameters.AddWithValue("@userId", id);

            await using var leitorRoles = await comandoRoles.ExecuteReaderAsync(ct);
            while (await leitorRoles.ReadAsync(ct))
            {
                if (!leitorRoles.IsDBNull(0))
                    roles.Add(leitorRoles.GetString(0));
            }
        }

        return new UsuarioAutenticavel(id, userName, emailBanco, passwordHash, tipo, ativo, roles);
    }
}
