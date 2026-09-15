using System.Text.Json.Serialization;

namespace MechanicLtda.Auth.Lambda;

public sealed class LoginRequest
{
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("senha")]
    public string? Senha { get; set; }
}

/// <summary>Corpo de <c>POST /auth/cpf</c>. Aceita o CPF com ou sem máscara.</summary>
public sealed class CpfRequest
{
    [JsonPropertyName("cpf")]
    public string? Cpf { get; set; }
}

/// <summary>
/// Resposta de <c>POST /auth/cpf</c>: o token e o cliente identificado, para o
/// consumidor já saber qual <c>clienteId</c> usar nas rotas protegidas.
/// </summary>
public sealed class TokenClienteResponse
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("expiracao")]
    public DateTime Expiracao { get; set; }

    [JsonPropertyName("cliente")]
    public ClienteResumo Cliente { get; set; } = new();
}

public sealed class ClienteResumo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("nome")]
    public string Nome { get; set; } = string.Empty;
}

public sealed class TokenResponse
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("expiracao")]
    public DateTime Expiracao { get; set; }
}

public sealed class ErroResponse
{
    [JsonPropertyName("mensagem")]
    public string Mensagem { get; set; } = string.Empty;
}

/// <summary>Usuario como gravado pelo Identity em AspNetUsers.</summary>
public sealed record UsuarioAutenticavel(
    string Id,
    string UserName,
    string Email,
    string PasswordHash,
    int Tipo,
    bool Ativo,
    IReadOnlyList<string> Roles);

/// <summary>
/// Cliente como gravado pela aplicação em Clientes. Só o que o token precisa:
/// a identidade, o nome para exibição e o status que autoriza (ou não) o acesso.
/// </summary>
public sealed record ClienteAutenticavel(
    int Id,
    string Nome,
    bool Ativo);

/// <summary>Espelha MechanicLtda.Domain.Enums.TipoUsuario (claim "tipo").</summary>
public static class TipoUsuario
{
    public static string Nome(int tipo) => tipo switch
    {
        1 => "Administrador",
        2 => "Funcionario",
        3 => "Cliente",
        _ => tipo.ToString(),
    };
}
