namespace MechanicLtda.Auth.Lambda;

/// <summary>Consulta de usuarios (funcionarios) para o login por e-mail e senha.</summary>
public interface IUsuarioRepository
{
    Task<UsuarioAutenticavel?> BuscarPorEmailAsync(string email, CancellationToken ct = default);
}

/// <summary>Consulta de clientes pelo indice cego do CPF/CNPJ.</summary>
public interface IClienteRepository
{
    Task<ClienteAutenticavel?> BuscarPorHashAsync(string hash, CancellationToken ct = default);
}

/// <summary>
/// O que a funcao de autenticacao precisa: os repositorios e a configuracao do token.
/// Em producao vem das variaveis de ambiente (<see cref="DoAmbiente"/>).
/// </summary>
public sealed record DependenciasAutenticacao(
    IUsuarioRepository Usuarios,
    IClienteRepository Clientes,
    JwtTokenService TokenService,
    string? CpfHashKey,
    string IssuerCpf,
    int ExpiracaoCpfMinutos)
{
    public static DependenciasAutenticacao DoAmbiente()
    {
        var connectionString = LerObrigatoria("DB_CONNECTION_STRING");
        var jwtSecretKey = LerObrigatoria("JWT_SECRET_KEY");

        var tokenService = new JwtTokenService(
            jwtSecretKey,
            Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "MechanicLtda.API",
            Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "MechanicLtda.Clients",
            int.TryParse(Environment.GetEnvironmentVariable("JWT_EXPIRACAO_MINUTOS"), out var minutos) ? minutos : 60);

        return new DependenciasAutenticacao(
            new UsuarioRepository(connectionString),
            new ClienteRepository(connectionString),
            tokenService,
            // A chave do indice cego nao e obrigatoria de proposito: se faltar, so a
            // rota /auth/cpf fica indisponivel (com log explicito) - o login por
            // e-mail e senha continua de pe.
            Environment.GetEnvironmentVariable("CPF_HASH_KEY"),
            Environment.GetEnvironmentVariable("JWT_ISSUER_CPF") ?? "MechanicLtda.Auth.Cpf",
            // Janela curta: o token de cliente e obtido so com o CPF, entao vale menos
            // tempo que o token de funcionario (que exige senha).
            int.TryParse(Environment.GetEnvironmentVariable("JWT_CPF_EXPIRACAO_MINUTOS"), out var m) ? m : 30);
    }

    internal static string LerObrigatoria(string nome) =>
        Environment.GetEnvironmentVariable(nome)
        ?? throw new InvalidOperationException($"Variavel de ambiente '{nome}' nao configurada na funcao.");
}
