using System.Text.RegularExpressions;
using Xunit;

namespace ActionLedger.Architecture.Tests;

/// <summary>
/// AD-17 and AD-16 — the compose topology, the two new Dockerfiles, the nginx template, and
/// <c>.env.example</c>, pinned as text.
///
/// None of this is observable from an assembly. No type changes when
/// <c>service_completed_successfully</c> weakens to <c>service_started</c>, when
/// <c>extra_hosts</c> disappears, when a proxy timeout drops below the 200 seconds NFR-1 allows a
/// local model, or when a configuration key falls out of <c>.env.example</c> — and every one of
/// those produces a stack that starts and then behaves wrongly. Without these assertions AD-17 has
/// no enforcement at all.
///
/// The assertions pin literal text rather than parsing YAML on purpose: the spine's Stack table is
/// the package allowlist, and adding a YAML package for one test is a bigger deviation than a
/// regex. <c>ThemeAndTokenTests</c> already pins 40 literal values out of a non-C# file, so the
/// shape is established. Every compose assertion is scoped to one service block, so a match can
/// never come from the wrong service.
/// </summary>
public sealed class ComposeTopologyTests
{
    private const string ComposeFile = "docker-compose.yml";
    private const string EnvExample = ".env.example";
    private const string ApiDockerfile = "src/ActionLedger.Api/Dockerfile";
    private const string MigrateDockerfile = "src/ActionLedger.Api/Dockerfile.migrate";
    private const string WebDockerfile = "src/ActionLedger.Web/Dockerfile";
    private const string NginxTemplate = "src/ActionLedger.Web/nginx.conf.template";
    private const string CiWorkflow = ".github/workflows/ci.yml";
    private const string PostgresFixture = "tests/Infrastructure.Tests/PostgresFixture.cs";

    /// <summary>AD-17's four services, in the order the dependency chain runs them.</summary>
    private static readonly string[] TheFourServices = ["db", "migrate", "api", "web"];

    /// <summary>The paths nginx forwards to the api, in template order.</summary>
    private static readonly string[] TheProxiedPaths = ["/api/", "/swagger", "/openapi", "/health"];

    /// <summary>
    /// The spine's Config keys row, in the <c>__</c> form an environment variable carries. .NET
    /// maps <c>__</c> to the <c>:</c> in a configuration key; <c>API_UPSTREAM</c> is read by the
    /// web image's entrypoint rather than by the host, so it has no <c>:</c> form to convert.
    /// </summary>
    public static TheoryData<string> SpineConfigKeys => new()
    {
        "Ai__Provider",
        "Ai__PromptVersion",
        "Ai__CallTimeoutSeconds",
        "Ai__LowConfidenceThreshold",
        "Ai__LocalOpenAI__BaseUrl",
        "Ai__LocalOpenAI__Model",
        "Ai__AzureOpenAI__Endpoint",
        "Ai__AzureOpenAI__Model",
        "Ai__AzureOpenAI__ApiKey",
        "Jwt__Key",
        "Jwt__Issuer",
        "Database__ConnectionString",
        "Webhooks__RetryDelaysSeconds",
        "Webhooks__TimeoutSeconds",
        "Webhooks__LeaseSeconds",
        "Seed__Enabled",
        "API_UPSTREAM",
    };

    /// <summary>
    /// Every key in <c>.env.example</c> whose real value is a credential. NFR5 — the file carries
    /// placeholders only, and a placeholder that stops saying "replace" is a value someone pasted.
    /// </summary>
    public static TheoryData<string> SecretKeys => new()
    {
        "Jwt__Key",
        "Seed__DefaultPassword",
        "Ai__AzureOpenAI__ApiKey",
        "POSTGRES_PASSWORD",
    };

    /// <summary>The two timeout directives NFR-1 puts on every proxied location.</summary>
    public static TheoryData<string> ProxyTimeouts => new()
    {
        "proxy_read_timeout 200s;",
        "proxy_send_timeout 200s;",
    };

    // --- .env.example (AD-16) ---------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(SpineConfigKeys))]
    public void Every_spine_config_key_has_a_placeholder_in_the_example_env_file(string key)
    {
        // Webhooks__RetryDelaysSeconds is an array, so it appears as __0, __1, ... — the key is
        // present when at least one assignment starts with it.
        bool assigned = Regex.IsMatch(
            Read(EnvExample),
            $@"^{Regex.Escape(key)}(__\d+)?=",
            RegexOptions.Multiline);

        Assert.True(
            assigned,
            $"{EnvExample} assigns no {key}. AD-16 requires every key from the spine's Config keys "
                + "row to carry a placeholder there, or a clean clone has no way to learn it exists.");
    }

    [Fact]
    public void The_example_env_file_defaults_the_ai_provider_to_fake() =>
        // AD-16 — Fake answers from the committed fixtures, so the demo runs with no model server
        // and no credential.
        Assert.Equal("Fake", EnvValue("Ai__Provider"));

    [Theory]
    [MemberData(nameof(SecretKeys))]
    public void Every_secret_placeholder_still_reads_as_a_placeholder(string key)
    {
        string value = EnvValue(key);

        Assert.True(
            value.Contains("replace", StringComparison.OrdinalIgnoreCase),
            $"{EnvExample} gives {key} the value \"{value}\", which does not read as a placeholder. "
                + "NFR5 — no secret is committed, and this file carries placeholders only.");
    }

    [Fact]
    public void The_db_service_credentials_agree_with_the_connection_string()
    {
        // The most likely first-run failure there is. `db` creates its role and database from the
        // POSTGRES_* trio and the api connects with what Database__ConnectionString says, so a
        // divergence surfaces as an authentication error from the api long after migrate failed.
        string connectionString = EnvValue("Database__ConnectionString");

        Assert.Equal("db", ConnectionStringPart(connectionString, "Host"));
        Assert.Equal(EnvValue("POSTGRES_USER"), ConnectionStringPart(connectionString, "Username"));
        Assert.Equal(EnvValue("POSTGRES_DB"), ConnectionStringPart(connectionString, "Database"));
        Assert.Equal(EnvValue("POSTGRES_PASSWORD"), ConnectionStringPart(connectionString, "Password"));
    }

    // --- docker-compose.yml (AD-17) ---------------------------------------------------------------

    [Fact]
    public void Compose_declares_exactly_the_four_services_ad17_names()
    {
        // `receiver` is Story 5.2, and its keys are absent from the spine's Config keys row, so it
        // has no business here yet. A fifth service of any kind is a topology change AD-17 approves.
        string[] declared = [.. Services().Select(service => service.Name)];

        Assert.Equal(TheFourServices, declared);
    }

    [Fact]
    public void The_db_service_runs_the_postgres_image_the_tests_run()
    {
        // PostgresFixture says "The image matches the compose service", and the tag is read out of
        // it rather than repeated here: a second copy would let the fixture move on its own, and
        // then the migration would be proven against a server the demo does not run.
        Match fixtureTag = Regex.Match(Read(PostgresFixture), @"new PostgreSqlBuilder\(""(?<tag>[^""]+)""\)");

        Assert.True(fixtureTag.Success, $"{PostgresFixture} names no PostgreSQL image.");

        Assert.Contains(
            $"image: {fixtureTag.Groups["tag"].Value}",
            ServiceBlock("db"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_migrate_service_builds_from_its_own_dockerfile_and_runs_once()
    {
        string migrate = ServiceBlock("migrate");

        Assert.Contains($"dockerfile: {MigrateDockerfile}", migrate, StringComparison.Ordinal);
        AssertDependsOn(migrate, "migrate", "db", "service_healthy");
        // A restart policy would turn a one-shot into a loop, and `api` would never see it complete.
        Assert.Contains("restart: \"no\"", migrate, StringComparison.Ordinal);
        // The bundle is the entrypoint; the connection string is its only argument.
        Assert.Contains("--connection", migrate, StringComparison.Ordinal);
    }

    [Fact]
    public void The_api_service_starts_only_after_migrate_exits_zero()
    {
        // The whole ordering guarantee. `service_started` would let the api — and the seeder inside
        // it, which does not wait for migrate and assumes the tables exist — race the schema.
        AssertDependsOn(ServiceBlock("api"), "api", "migrate", "service_completed_successfully");
    }

    [Fact]
    public void The_api_service_resolves_the_host_gateway() =>
        // AD-17 — so a local model server on the developer's machine resolves from inside the
        // container on Linux engines as well as on Docker Desktop.
        Assert.Contains(
            "\"host.docker.internal:host-gateway\"",
            ServiceBlock("api"),
            StringComparison.Ordinal);

    [Fact]
    public void The_api_service_runs_in_the_compose_environment() =>
        // AD-13 — the only value besides Development that serves Swagger UI, and browsing /swagger
        // is an acceptance criterion. ApiEnvironments.Compose is the other half of this pin.
        Assert.Contains(
            "ASPNETCORE_ENVIRONMENT: Compose",
            ServiceBlock("api"),
            StringComparison.Ordinal);

    [Fact]
    public void The_api_healthcheck_is_the_liveness_endpoint()
    {
        // AD-17 names /health, which touches no dependency, so the check never reports the api down
        // because PostgreSQL is slow. The readiness endpoint carries a 5-second database probe and
        // is the Container Apps readiness probe, not this one.
        string api = ServiceBlock("api");

        Assert.Contains("healthcheck:", api, StringComparison.Ordinal);

        string probe = api
            .Split('\n')
            .Single(line => line.Contains("test:", StringComparison.Ordinal));

        Assert.Contains("http://localhost:8080/health\"", probe, StringComparison.Ordinal);
        Assert.DoesNotContain("/health/ready", probe, StringComparison.Ordinal);
    }

    [Fact]
    public void The_web_service_is_the_one_published_port()
    {
        string web = ServiceBlock("web");

        Assert.Contains("8080:80", web, StringComparison.Ordinal);
        Assert.Contains("API_UPSTREAM", web, StringComparison.Ordinal);
        AssertDependsOn(web, "web", "api", "service_healthy");

        // Single origin is the whole point: the browser calls /api/v1/... on the web origin and
        // nginx forwards it. A published api port invites a second origin, and CORS with it.
        int publishedPorts = Regex.Matches(
            Read(ComposeFile),
            @"^\s+- ""\d+:\d+""\s*$",
            RegexOptions.Multiline).Count;

        Assert.Equal(1, publishedPorts);
    }

    [Theory]
    [InlineData("migrate")]
    [InlineData("api")]
    public void Every_service_that_reads_the_configuration_declares_the_env_file_as_required(string service) =>
        // AD-16 — compose auto-loads `.env` for interpolation only. The env_file declaration is
        // what puts the keys inside a container, and `required: true` is what turns an absent
        // `.env` into a named failure instead of a half-configured start.
        Assert.Matches(@"env_file:\s*- path: \.env\s*required: true", ServiceBlock(service));

    [Theory]
    [InlineData("db")]
    [InlineData("web")]
    public void No_service_holds_secrets_it_does_not_read(string service)
    {
        // NFR5 — `env_file` hands over the whole file. `db` reads three keys and `web` reads one,
        // so handing either the file would put Jwt__Key, Seed__DefaultPassword and the Azure key
        // inside a database container and inside the one container the host port is published on.
        string block = ServiceBlock(service);

        Assert.DoesNotContain("env_file", block, StringComparison.Ordinal);
        Assert.Contains("environment:", block, StringComparison.Ordinal);
    }

    [Fact]
    public void The_db_service_fails_by_name_rather_than_starting_on_a_blank_credential()
    {
        // An unguarded ${...} interpolates to an empty string, compose only warns, and the stack
        // comes up with a database whose password is blank — or a bundle run against "".
        string db = ServiceBlock("db");

        foreach (string key in (string[])["POSTGRES_USER", "POSTGRES_DB", "POSTGRES_PASSWORD"])
        {
            Assert.Contains($"${{{key}:?", db, StringComparison.Ordinal);
        }

        Assert.Contains("${Database__ConnectionString:?", ServiceBlock("migrate"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_db_healthcheck_asks_over_tcp()
    {
        // initdb runs a temporary server with listen_addresses='', so a socket-only pg_isready can
        // report healthy while TCP is still closed — and `migrate` then fails connection refused.
        string probe = ServiceBlock("db")
            .Split('\n')
            .Single(line => line.Contains("test:", StringComparison.Ordinal));

        Assert.Contains("pg_isready -h 127.0.0.1", probe, StringComparison.Ordinal);
    }

    [Fact]
    public void The_web_service_reports_when_nginx_is_serving() =>
        // Without it `docker compose up --wait` returns before nginx answers and CI's smoke step
        // has nothing to gate on. nginx:alpine ships busybox wget and no curl.
        Assert.Contains("wget", ServiceBlock("web"), StringComparison.Ordinal);

    // --- the web image (AD-16, NFR-1, NFR-10) ------------------------------------------------------

    [Fact]
    public void The_web_image_is_nginx_serving_static_assets()
    {
        string dockerfile = Read(WebDockerfile);

        Assert.Contains("FROM nginx:1.30-alpine", dockerfile, StringComparison.Ordinal);
        // Only wwwroot/ is served: the publish root also holds openapi.json, an IIS web.config, and
        // the runtimeconfig, none of which belong on a public origin.
        Assert.Contains("/app/publish/wwwroot/", dockerfile, StringComparison.Ordinal);
        Assert.Contains("/etc/nginx/templates/default.conf.template", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void No_image_stage_is_built_on_a_node_base()
    {
        // NFR-10 and the 2026-09-21 sprint change — the demo needs Docker Desktop only, and no
        // Node, npm, or TypeScript tooling appears anywhere in the build.
        string[] stages =
        [
            .. new[] { ApiDockerfile, MigrateDockerfile, WebDockerfile }
                .SelectMany(dockerfile => Read(dockerfile).Split('\n'))
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("FROM ", StringComparison.Ordinal)),
        ];

        Assert.DoesNotContain(stages, stage => stage.Contains("node", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Every_proxied_location_re_resolves_its_upstream()
    {
        // A literal proxy_pass is resolved once, at config load, and held for the life of the
        // process. Recreating `api` moves it to a new address and every proxied request 502s while
        // compose reports every service up — and `docker compose up -d` does not recreate `web`,
        // so the stack stays broken with no signal. Naming the upstream through a variable forces
        // a per-request lookup; $request_uri is required because a variable proxy_pass stops
        // passing the request URI implicitly.
        foreach ((string path, string body) in ProxiedLocations())
        {
            Assert.True(
                body.Contains("set $upstream ${API_UPSTREAM};", StringComparison.Ordinal)
                    && body.Contains("proxy_pass $upstream$request_uri;", StringComparison.Ordinal),
                $"The `location {path}` block in {NginxTemplate} does not proxy through a variable. "
                    + "A literal proxy_pass pins the api's address at config load.");
        }

        // The per-request lookup needs somewhere to ask. Docker's embedded DNS is 127.0.0.11; the
        // image default is templated so a cloud deployment can point at its own.
        Assert.Contains(
            "resolver ${NGINX_RESOLVER} valid=10s ipv6=off;",
            Read(NginxTemplate),
            StringComparison.Ordinal);

        Assert.Contains("ENV NGINX_RESOLVER=127.0.0.11", Read(WebDockerfile), StringComparison.Ordinal);
    }

    [Fact]
    public void The_published_asset_directories_404_rather_than_falling_through_to_the_spa()
    {
        // _framework/ and _content/ hold fingerprinted files, not routes. Answered by the SPA rule
        // a missing one comes back as index.html with a 200, and a stale deploy surfaces as an
        // unreadable wasm or a JSON parse error instead of a clean 404.
        string template = Read(NginxTemplate);

        Assert.Matches(@"location /_framework/ \{\s*try_files \$uri =404;", template);
        Assert.Matches(@"location /_content/ \{\s*try_files \$uri =404;", template);
    }

    [Fact]
    public void The_published_origin_does_not_advertise_its_server_version() =>
        // The one host-published port, on every response and every error page.
        Assert.Contains("server_tokens off;", Read(NginxTemplate), StringComparison.Ordinal);

    [Fact]
    public void The_web_image_restricts_envsubst_to_the_templated_variables() =>
        // Without the filter, nginx's entrypoint substitutes every $name in the template and blanks
        // out $uri, $host, $http_host, $scheme, $remote_addr and $proxy_add_x_forwarded_for. Set in
        // the Dockerfile rather than in compose so the property travels with the image to cloud.
        Assert.Contains(
            "NGINX_ENVSUBST_FILTER='^(API_UPSTREAM|NGINX_RESOLVER)$'",
            Read(WebDockerfile),
            StringComparison.Ordinal);

    [Fact]
    public void The_proxy_covers_exactly_the_api_paths_and_nothing_else()
    {
        // /health is proxied because the generated client exposes GetHealthAsync and
        // GetReadinessAsync, and the SPA fallback would otherwise answer them with a page of HTML.
        string[] proxied = [.. ProxiedLocations().Select(location => location.Path)];

        Assert.Equal(TheProxiedPaths, proxied);
    }

    [Theory]
    [MemberData(nameof(ProxyTimeouts))]
    public void Every_proxied_location_holds_the_connection_for_the_nfr1_budget(string directive)
    {
        // NFR-1 — a run makes at most two provider calls of Ai:CallTimeoutSeconds each, so a local
        // model can take most of three minutes. The proxy is never what cuts an extraction off.
        foreach ((string path, string body) in ProxiedLocations())
        {
            Assert.True(
                body.Contains(directive, StringComparison.Ordinal),
                $"The `location {path}` block in {NginxTemplate} does not declare `{directive}`. "
                    + "NFR-1 gives every proxied path 200 seconds of read and send timeout.");
        }
    }

    [Fact]
    public void The_proxy_reproduces_the_dev_servers_404_for_the_startup_config_probe()
    {
        // Load-bearing, not defensive. WebAssemblyHostBuilder.CreateDefault fetches
        // appsettings.json over HTTP before the app starts and tolerates a 404 — which is what the
        // dev server returns, because the file does not exist. Answered with index.html and a 200
        // instead, the JSON parse throws and the login page never renders.
        string template = Read(NginxTemplate);

        AssertReturnsNotFound(template, "location = /appsettings.json {");
        AssertReturnsNotFound(template, @"location ~ ^/appsettings\..+\.json$ {");
    }

    [Fact]
    public void The_proxy_falls_back_to_index_html_for_every_unknown_path()
    {
        // App.razor routes only / and /login today; /meetings and /actions reach the in-app Not
        // found notice. Routes are not whitelisted — nginx 200-falls-back and Blazor decides.
        string template = Read(NginxTemplate);

        Assert.Contains("try_files $uri $uri/ /index.html;", template, StringComparison.Ordinal);
        // The publish output ships a .gz beside every framework asset.
        Assert.Contains("gzip_static on;", template, StringComparison.Ordinal);
        // The three ICU payloads under _framework/ are .dat, which the stock mime map does not know.
        Assert.Contains("application/octet-stream dat;", template, StringComparison.Ordinal);
    }

    // --- the images (AD-17, NFR-11) ----------------------------------------------------------------

    [Theory]
    [InlineData(ApiDockerfile)]
    [InlineData(MigrateDockerfile)]
    [InlineData(WebDockerfile)]
    public void Every_image_builds_on_the_sdk_patch_global_json_pins(string dockerfile)
    {
        // global.json sets rollForward: disable, so the floating `10.0` tag breaks every image
        // build the day it moves past the pinned patch — silently, on the next base-image refresh.
        Match version = Regex.Match(Read("global.json"), @"""version""\s*:\s*""(?<version>[^""]+)""");

        Assert.True(version.Success, "global.json declares no SDK version.");

        Assert.Contains(
            $"FROM mcr.microsoft.com/dotnet/sdk:{version.Groups["version"].Value} AS build",
            Read(dockerfile),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_api_image_carries_the_binary_its_healthcheck_runs() =>
        // aspnet:10.0 is Debian slim and ships neither curl nor wget, so without this the
        // healthcheck AD-17 mandates has nothing to run, the api never reports healthy, and `web`
        // never starts either.
        Assert.Matches(@"apt-get install [^\n]*\bcurl\b", Read(ApiDockerfile));

    [Fact]
    public void The_migrate_image_is_the_bundle_and_nothing_else()
    {
        string dockerfile = Read(MigrateDockerfile);

        Assert.Contains("ENTRYPOINT [\"/app/migrate/efbundle\"]", dockerfile, StringComparison.Ordinal);
        // Npgsql's GSSAPI probe writes a scary-looking line to stderr without this, right where a
        // reader would expect the migration to be failing.
        Assert.Contains("libgssapi-krb5-2", dockerfile, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("build")]
    [InlineData("bundle")]
    public void The_migrate_image_shares_the_api_images_stage(string stage)
    {
        // Docker cannot COPY --from a stage that lives in another file, so these two stages are
        // duplicated. They are only free — one layer cache, and one SHA for the schema and the code
        // that expects it — while they stay byte-identical.
        Assert.Equal(Stage(Read(ApiDockerfile), stage), Stage(Read(MigrateDockerfile), stage));
    }

    [Theory]
    [InlineData(ApiDockerfile)]
    [InlineData(MigrateDockerfile)]
    [InlineData(WebDockerfile)]
    public void Ci_builds_every_image(string dockerfile) =>
        // NFR-11 — the pipeline is a deliverable, and AD-17's topology is only real if it builds.
        Assert.Contains(
            $"docker build --file {dockerfile} ",
            Read(CiWorkflow),
            StringComparison.Ordinal);

    [Fact]
    public void Ci_proves_the_compose_file_parses_and_publishes_nothing()
    {
        string workflow = Read(CiWorkflow);

        // `.env.example` is placeholders only, which is exactly what a parse-and-interpolate check
        // needs: every ${...} resolves and the required env_file is found, with no real secret.
        Assert.Contains("docker compose config --quiet", workflow, StringComparison.Ordinal);
        Assert.Contains("cp .env.example .env", workflow, StringComparison.Ordinal);

        // CI builds images; GHCR and Azure are cd.yml, which is a later story.
        Assert.DoesNotContain("docker push", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("ghcr.io", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Ci_runs_the_stack_and_probes_it()
    {
        // Everything else in this class pins text. A trailing slash on proxy_pass is a
        // one-character edit that keeps every text assertion green and ships a login page that
        // cannot log in, so the pipeline has to start the stack and ask it questions.
        string workflow = Read(CiWorkflow);

        Assert.Contains("docker compose up -d --wait", workflow, StringComparison.Ordinal);
        // The SPA is served, Blazor's startup config probe 404s instead of getting HTML, the api
        // answers through the proxy, and /api/v1 reaches the API rather than the SPA fallback.
        Assert.Contains("http://localhost:8080/appsettings.json", workflow, StringComparison.Ordinal);
        Assert.Contains("http://localhost:8080/health", workflow, StringComparison.Ordinal);
        Assert.Contains("http://localhost:8080/api/v1/users", workflow, StringComparison.Ordinal);
        Assert.Contains("docker compose down -v", workflow, StringComparison.Ordinal);
    }

    // --- reading the files -------------------------------------------------------------------------

    /// <summary>
    /// One root-finder per assembly: <c>ProjectFile.RepositoryRoot</c> walks up to
    /// <c>ActionLedger.sln</c>, and <c>WebStructureTests</c> already shares it.
    /// </summary>
    private static string Read(string relativePath)
    {
        string path = Path.Combine(ProjectFile.RepositoryRoot.FullName, relativePath);

        Assert.True(
            File.Exists(path),
            $"Expected {relativePath} at {path}. AD-17's topology is missing a file.");

        return File.ReadAllText(path);
    }

    /// <summary>The value of one assignment in <c>.env.example</c>.</summary>
    private static string EnvValue(string key)
    {
        Match assignment = Regex.Match(
            Read(EnvExample),
            $@"^{Regex.Escape(key)}=(?<value>.*)$",
            RegexOptions.Multiline);

        Assert.True(assignment.Success, $"{EnvExample} assigns no {key}.");

        return assignment.Groups["value"].Value.Trim();
    }

    /// <summary>One <c>Key=Value</c> pair out of an Npgsql connection string.</summary>
    private static string ConnectionStringPart(string connectionString, string key)
    {
        Match part = Regex.Match(
            connectionString,
            $@"(?:^|;)\s*{Regex.Escape(key)}\s*=\s*(?<value>[^;]*)");

        Assert.True(part.Success, $"Database__ConnectionString declares no {key}.");

        return part.Groups["value"].Value.Trim();
    }

    /// <summary>
    /// One <c>depends_on</c> edge, named on both ends. A bare <c>Assert.Contains("condition: …")</c>
    /// stays green when the dependency is repointed at a different service.
    /// </summary>
    private static void AssertDependsOn(string block, string service, string dependency, string condition)
    {
        bool declared = Regex.IsMatch(
            block,
            $@"depends_on:\s*{Regex.Escape(dependency)}:\s*condition:\s*{Regex.Escape(condition)}\b");

        Assert.True(
            declared,
            $"The {service} service does not declare `depends_on: {dependency}: condition: {condition}`.");
    }

    private static void AssertReturnsNotFound(string template, string header) =>
        Assert.Matches(Regex.Escape(header) + @"\s*return 404;", template);

    /// <summary>
    /// The compose services, in file order, each with its own body. Scoping every assertion to one
    /// block is what stops a match coming from the wrong service.
    /// </summary>
    private static IReadOnlyList<(string Name, string Body)> Services()
    {
        string[] lines = Read(ComposeFile).Split('\n');
        int start = Array.FindIndex(lines, line => line.StartsWith("services:", StringComparison.Ordinal));

        Assert.True(start >= 0, $"{ComposeFile} declares no services.");

        List<(string Name, string Body)> services = [];
        string? current = null;
        List<string> body = [];

        for (int index = start + 1; index < lines.Length; index++)
        {
            string line = lines[index];

            // A non-blank line at column zero is the next top-level key, so the mapping is over.
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
            {
                break;
            }

            Match header = Regex.Match(line, @"^  (?<name>[A-Za-z0-9_.-]+):\s*$");

            if (header.Success)
            {
                if (current is not null)
                {
                    services.Add((current, string.Join('\n', body)));
                }

                current = header.Groups["name"].Value;
                body = [];
                continue;
            }

            // The `# --- api ---` banners sit at two-space indent between services, so without
            // this one service's body absorbs the next one's prose and a scoped assertion could
            // match text that belongs to a different service.
            if (current is not null && !line.TrimStart().StartsWith('#'))
            {
                body.Add(line);
            }
        }

        if (current is not null)
        {
            services.Add((current, string.Join('\n', body)));
        }

        return services;
    }

    private static string ServiceBlock(string name)
    {
        string[] bodies =
        [
            .. Services()
                .Where(service => string.Equals(service.Name, name, StringComparison.Ordinal))
                .Select(service => service.Body),
        ];

        Assert.True(bodies.Length == 1, $"{ComposeFile} declares {bodies.Length} `{name}` services; expected 1.");

        return bodies[0];
    }

    /// <summary>
    /// Every <c>location</c> block in the nginx template that proxies, in file order, each with its
    /// own body — the same "scope the assertion to the block" discipline the compose parsing uses.
    /// </summary>
    private static IReadOnlyList<(string Path, string Body)> ProxiedLocations()
    {
        List<(string Path, string Body)> locations = [];
        string[] lines = Read(NginxTemplate).Split('\n');

        for (int index = 0; index < lines.Length; index++)
        {
            Match header = Regex.Match(lines[index], @"^    location (?<path>\S+) \{\s*$");

            if (!header.Success)
            {
                continue;
            }

            List<string> body = [];

            for (int inner = index + 1;
                inner < lines.Length && !string.Equals(lines[inner].TrimEnd(), "    }", StringComparison.Ordinal);
                inner++)
            {
                body.Add(lines[inner]);
            }

            string text = string.Join('\n', body);

            if (text.Contains("proxy_pass", StringComparison.Ordinal))
            {
                locations.Add((header.Groups["path"].Value, text));
            }
        }

        return locations;
    }

    /// <summary>
    /// One named Dockerfile stage, comments and blank lines stripped: the instructions that decide
    /// whether two builds share a layer cache.
    /// </summary>
    private static string Stage(string dockerfile, string name)
    {
        string[] instructions =
        [
            .. dockerfile
                .Split('\n')
                .Select(line => line.TrimEnd())
                .Where(line => line.Length > 0 && !line.StartsWith('#')),
        ];

        int start = Array.FindIndex(
            instructions,
            line => line.StartsWith("FROM ", StringComparison.Ordinal)
                && line.EndsWith($" AS {name}", StringComparison.Ordinal));

        Assert.True(start >= 0, $"The Dockerfile declares no `{name}` stage.");

        int next = Array.FindIndex(
            instructions,
            start + 1,
            line => line.StartsWith("FROM ", StringComparison.Ordinal));

        return string.Join('\n', instructions[start..(next < 0 ? instructions.Length : next)]);
    }
}
