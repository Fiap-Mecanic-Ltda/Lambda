using System.Text.Json.Serialization;

namespace MechanicLtda.Auth.Lambda;

public sealed class LoginRequest
{
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("senha")]
    public string? Senha { get; set; }
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
