using System.Text;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.TestUtilities;
using MechanicLtda.Auth.Lambda;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace MechanicLtda.Auth.Lambda.Tests;

/// <summary>
/// Handler das rotas /auth/login e /auth/cpf com repositórios em memória: cobre as
/// respostas que o API Gateway devolve ao consumidor (200, 400, 401, 500 e 503).
/// </summary>
public class FunctionTests
{
    private const string SecretKey = "ChaveDeTesteMechanicLtda_Minimo32Caracteres!";
    private const string ChaveCpf  = "chave-do-indice-cego-de-teste";
    private const string CpfValido = "529.982.247-25";

    private readonly UsuariosFalsos _usuarios = new();
    private readonly ClientesFalsos _clientes = new();
    private readonly TestLambdaContext _contexto = new();

    private Function Criar(string? chaveCpf = ChaveCpf) => new(new DependenciasAutenticacao(
        _usuarios, _clientes,
        new JwtTokenService(SecretKey, "MechanicLtda.API", "MechanicLtda.Clients", 60),
        chaveCpf, "MechanicLtda.Auth.Cpf", 30));

    private static APIGatewayHttpApiV2ProxyRequest Requisicao(string rota, string? corpo, bool base64 = false) => new()
    {
        RouteKey = rota,
        Body = corpo is not null && base64 ? Convert.ToBase64String(Encoding.UTF8.GetBytes(corpo)) : corpo,
        IsBase64Encoded = base64,
    };

    private static JsonElement Corpo(APIGatewayHttpApiV2ProxyResponse resposta) =>
        JsonDocument.Parse(resposta.Body).RootElement;

    private UsuarioAutenticavel CadastrarUsuario(string senha, bool ativo = true)
    {
        var semHash = new UsuarioAutenticavel("u1", "maria", "maria@oficina.com", string.Empty, 2, ativo, ["Funcionario"]);
        var usuario = semHash with { PasswordHash = new PasswordHasher<UsuarioAutenticavel>().HashPassword(semHash, senha) };
        _usuarios.Cadastrados["maria@oficina.com"] = usuario;
        return usuario;
    }

    // ── POST /auth/login ────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Login_com_senha_certa_devolve_token(bool base64)
    {
        CadastrarUsuario("Senha@123");

        var resposta = await Criar().FunctionHandler(
            Requisicao("POST /auth/login", """{"email":"maria@oficina.com","senha":"Senha@123"}""", base64), _contexto);

        Assert.Equal(200, resposta.StatusCode);
        Assert.Equal("application/json", resposta.Headers["Content-Type"]);
        Assert.False(string.IsNullOrWhiteSpace(Corpo(resposta).GetProperty("token").GetString()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nao-e-json")]
    [InlineData("""{"email":"maria@oficina.com"}""")]
    [InlineData("""{"senha":"x"}""")]
    public async Task Login_sem_email_ou_senha_responde_400(string? corpo)
    {
        var resposta = await Criar().FunctionHandler(Requisicao("POST /auth/login", corpo), _contexto);

        Assert.Equal(400, resposta.StatusCode);
        Assert.Equal("Informe e-mail e senha.", Corpo(resposta).GetProperty("mensagem").GetString());
    }

    [Fact]
    public async Task Login_de_usuario_inexistente_inativo_ou_senha_errada_responde_401_igual()
    {
        var corpo = """{"email":"maria@oficina.com","senha":"Senha@123"}""";

        var inexistente = await Criar().FunctionHandler(Requisicao("POST /auth/login", corpo), _contexto);

        CadastrarUsuario("Senha@123", ativo: false);
        var inativo = await Criar().FunctionHandler(Requisicao("POST /auth/login", corpo), _contexto);

        CadastrarUsuario("OutraSenha");
        var senhaErrada = await Criar().FunctionHandler(Requisicao("POST /auth/login", corpo), _contexto);

        _usuarios.Cadastrados["maria@oficina.com"] = new UsuarioAutenticavel("u1", "maria", "maria@oficina.com", string.Empty, 2, true, []);
        var semHash = await Criar().FunctionHandler(Requisicao("POST /auth/login", corpo), _contexto);

        foreach (var resposta in new[] { inexistente, inativo, senhaErrada, semHash })
        {
            Assert.Equal(401, resposta.StatusCode);
            Assert.Equal("Credenciais invalidas.", Corpo(resposta).GetProperty("mensagem").GetString());
        }
    }

    [Fact]
    public async Task Rota_vazia_cai_no_login_por_senha()
    {
        var resposta = await Criar().FunctionHandler(new APIGatewayHttpApiV2ProxyRequest(), _contexto);
        Assert.Equal(400, resposta.StatusCode);
    }

    [Fact]
    public async Task Falha_no_banco_responde_500_sem_vazar_detalhe()
    {
        _usuarios.Falhar = true;

        var resposta = await Criar().FunctionHandler(
            Requisicao("POST /auth/login", """{"email":"maria@oficina.com","senha":"x"}"""), _contexto);

        Assert.Equal(500, resposta.StatusCode);
        Assert.Equal("Erro ao processar a autenticacao.", Corpo(resposta).GetProperty("mensagem").GetString());
        Assert.Contains("Falha ao autenticar", ((TestLambdaLogger)_contexto.Logger).Buffer.ToString());
    }

    // ── POST /auth/cpf ──────────────────────────────────────────────────────

    [Fact]
    public async Task Cpf_de_cliente_ativo_devolve_token_e_cliente()
    {
        _clientes.Cadastrados[DocumentoHash.Gerar(CpfValido, ChaveCpf)] = new ClienteAutenticavel(42, "João", true);

        var resposta = await Criar().FunctionHandler(Requisicao("POST /auth/cpf", $$"""{"cpf":"{{CpfValido}}"}"""), _contexto);

        Assert.Equal(200, resposta.StatusCode);
        var corpo = Corpo(resposta);
        Assert.Equal(42, corpo.GetProperty("cliente").GetProperty("id").GetInt32());
        Assert.Equal("João", corpo.GetProperty("cliente").GetProperty("nome").GetString());
        Assert.False(string.IsNullOrWhiteSpace(corpo.GetProperty("token").GetString()));
    }

    [Theory]
    [InlineData("""{"cpf":"111.111.111-11"}""")]
    [InlineData("""{"cpf":""}""")]
    [InlineData(null)]
    public async Task Cpf_invalido_responde_400_sem_ir_ao_banco(string? corpo)
    {
        var resposta = await Criar().FunctionHandler(Requisicao("POST /auth/cpf", corpo), _contexto);

        Assert.Equal(400, resposta.StatusCode);
        Assert.Equal(0, _clientes.Consultas);
    }

    [Fact]
    public async Task Cpf_de_cliente_inexistente_ou_inativo_responde_401_igual()
    {
        var corpo = $$"""{"cpf":"{{CpfValido}}"}""";
        var inexistente = await Criar().FunctionHandler(Requisicao("POST /auth/cpf", corpo), _contexto);

        _clientes.Cadastrados[DocumentoHash.Gerar(CpfValido, ChaveCpf)] = new ClienteAutenticavel(42, "João", false);
        var inativo = await Criar().FunctionHandler(Requisicao("POST /auth/cpf", corpo), _contexto);

        Assert.Equal(401, inexistente.StatusCode);
        Assert.Equal(401, inativo.StatusCode);
        Assert.Equal(inexistente.Body, inativo.Body);
        Assert.DoesNotContain("529", ((TestLambdaLogger)_contexto.Logger).Buffer.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task Cpf_sem_chave_do_indice_cego_responde_503(string? chave)
    {
        var resposta = await Criar(chave).FunctionHandler(Requisicao("POST /auth/cpf", $$"""{"cpf":"{{CpfValido}}"}"""), _contexto);

        Assert.Equal(503, resposta.StatusCode);
    }

    [Fact]
    public async Task Rota_do_cpf_tambem_e_reconhecida_pelo_path()
    {
        var requisicao = new APIGatewayHttpApiV2ProxyRequest
        {
            RequestContext = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext
            {
                Http = new APIGatewayHttpApiV2ProxyRequest.HttpDescription { Path = "/AUTH/CPF" }
            },
            Body = """{"cpf":"123"}""",
        };

        Assert.Equal(400, (await Criar().FunctionHandler(requisicao, _contexto)).StatusCode);
    }

    // ── configuração pelo ambiente ──────────────────────────────────────────

    [Fact]
    public void Configuracao_do_ambiente_exige_banco_e_chave_do_jwt()
    {
        var anterior = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
        try
        {
            Environment.SetEnvironmentVariable("DB_CONNECTION_STRING", null);
            var erro = Assert.Throws<InvalidOperationException>(DependenciasAutenticacao.DoAmbiente);
            Assert.Contains("DB_CONNECTION_STRING", erro.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DB_CONNECTION_STRING", anterior);
        }
    }

    [Fact]
    public void Configuracao_do_ambiente_usa_os_padroes()
    {
        var variaveis = new Dictionary<string, string?>
        {
            ["DB_CONNECTION_STRING"] = "Server=localhost;Database=x;User Id=a;Password=b;",
            ["JWT_SECRET_KEY"] = SecretKey,
            ["CPF_HASH_KEY"] = ChaveCpf,
            ["JWT_ISSUER_CPF"] = null,
            ["JWT_CPF_EXPIRACAO_MINUTOS"] = "15",
            ["JWT_EXPIRACAO_MINUTOS"] = null,
        };
        var anteriores = variaveis.Keys.ToDictionary(k => k, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var (nome, valor) in variaveis)
                Environment.SetEnvironmentVariable(nome, valor);

            var dependencias = DependenciasAutenticacao.DoAmbiente();

            Assert.IsType<UsuarioRepository>(dependencias.Usuarios);
            Assert.IsType<ClienteRepository>(dependencias.Clientes);
            Assert.Equal(ChaveCpf, dependencias.CpfHashKey);
            Assert.Equal("MechanicLtda.Auth.Cpf", dependencias.IssuerCpf);
            Assert.Equal(15, dependencias.ExpiracaoCpfMinutos);
        }
        finally
        {
            foreach (var (nome, valor) in anteriores)
                Environment.SetEnvironmentVariable(nome, valor);
        }
    }

    private sealed class UsuariosFalsos : IUsuarioRepository
    {
        public Dictionary<string, UsuarioAutenticavel> Cadastrados { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool Falhar { get; set; }

        public Task<UsuarioAutenticavel?> BuscarPorEmailAsync(string email, CancellationToken ct = default)
        {
            if (Falhar)
                throw new InvalidOperationException("banco fora do ar");
            return Task.FromResult(Cadastrados.GetValueOrDefault(email));
        }
    }

    private sealed class ClientesFalsos : IClienteRepository
    {
        public Dictionary<string, ClienteAutenticavel> Cadastrados { get; } = new();
        public int Consultas { get; private set; }

        public Task<ClienteAutenticavel?> BuscarPorHashAsync(string hash, CancellationToken ct = default)
        {
            Consultas++;
            return Task.FromResult(Cadastrados.GetValueOrDefault(hash));
        }
    }
}
