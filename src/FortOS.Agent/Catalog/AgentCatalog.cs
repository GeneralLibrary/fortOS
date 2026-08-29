using System.Text.RegularExpressions;
using FortOS.Agent.Infrastructure;
using FortOS.Core;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace FortOS.Agent.Catalog;

/// <summary>
/// Agent template catalog based on local YAML files.
/// </summary>
public sealed partial class AgentCatalog : IAgentCatalog
{
    private static readonly Regex IdPattern = AgentIdRegex();
    private static readonly IReadOnlyDictionary<string, string> BuiltInTemplates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["nginx-basic"] = """
id: nginx-basic
name: Nginx Basic
logo: /logos/nginx.svg
version: 1.0.0
description: Minimal nginx static server template.
capabilities_required:
  - storage:share:media:read
parameters:
  - name: image
    type: string
    required: false
    default: nginx:alpine
compose:
  services:
    "{{.AgentId}}":
      image: "{{.ImageName}}"
      restart: unless-stopped
      tmpfs:
        - /var/cache/nginx:rw,noexec,nosuid,size=64m
      labels:
        fortos.template: nginx-basic
""",
        ["alpine-worker"] = """
id: alpine-worker
name: Alpine Worker
logo: /logos/alpine.svg
version: 1.0.0
description: Minimal long-running worker template.
capabilities_required:
  - storage:share:media:read
parameters:
  - name: image
    type: string
    required: false
    default: alpine:3.20
compose:
  services:
    "{{.AgentId}}":
      image: "{{.ImageName}}"
      command: ["/bin/sh", "-c", "while true; do sleep 3600; done"]
      restart: unless-stopped
      labels:
        fortos.template: alpine-worker
""",
        ["openclaw"] = """
id: openclaw
name: OpenClaw
logo: /logos/openclaw.svg
version: 1.0.0
description: OpenClaw — 开源通用 AI Agent 平台,支持 Telegram / Discord / API / Web 聊天接入,可连接主流 LLM。
capabilities_required:
  - storage:share:media:read
parameters:
  - name: image
    type: string
    required: false
    default: ghcr.io/openclaw/openclaw:latest
  - name: data_dir
    type: string
    required: false
    default: /home/node/.openclaw
  - name: data_uid
    type: int
    required: false
    default: "1000"
  - name: config_file
    type: string
    required: false
    default: openclaw.json
  - name: config_content
    type: text
    required: false
    default: |
      {
        gateway: { mode: "local" },
      }
  - name: HOST_PORT
    type: int
    required: false
    default: "18789"
  - name: CONTAINER_PORT
    type: int
    required: false
    default: "18789"
  - name: OPENAI_API_KEY
    type: string
    required: false
    default: ""
  - name: ANTHROPIC_API_KEY
    type: string
    required: false
    default: ""
  - name: TELEGRAM_BOT_TOKEN
    type: string
    required: false
    default: ""
  - name: DISCORD_BOT_TOKEN
    type: string
    required: false
    default: ""
  - name: OPENCLAW_GATEWAY_TOKEN
    type: string
    required: false
    default: fortos
access:
  - "Web/API 地址: http://<fortos-ip>:18789"
  - "网关访问 Token: 默认 fortos(WebUI 登录用),可在部署表单中修改"
  - "Telegram 接入: BotFather 创建 bot 后把 token 填入 TELEGRAM_BOT_TOKEN,编辑 /srv/nas/agents/<agent>/settings 重启生效"
  - "Discord 接入: 创建应用后填入 DISCORD_BOT_TOKEN"
  - "详细文档: https://openclaw.ai/docs"
compose:
  services:
    "{{.AgentId}}":
      image: "{{.ImageName}}"
      restart: unless-stopped
      ports:
        - "${HOST_PORT}:${CONTAINER_PORT}"
      environment:
        OPENAI_API_KEY: "${OPENAI_API_KEY}"
        ANTHROPIC_API_KEY: "${ANTHROPIC_API_KEY}"
        TELEGRAM_BOT_TOKEN: "${TELEGRAM_BOT_TOKEN}"
        DISCORD_BOT_TOKEN: "${DISCORD_BOT_TOKEN}"
        OPENCLAW_GATEWAY_TOKEN: "${OPENCLAW_GATEWAY_TOKEN}"
""",
        ["open-webui"] = """
id: open-webui
name: Open WebUI
logo: /logos/open-webui.png
version: 1.0.0
description: Open WebUI — 自托管的 LLM 聊天界面(兼容 Ollama / OpenAI 兼容 API),支持多人、RAG、插件。
capabilities_required:
  - storage:share:media:read
parameters:
  - name: image
    type: string
    required: false
    default: ghcr.io/open-webui/open-webui:main
  - name: data_dir
    type: string
    required: false
    default: /app/backend/data
  - name: HOST_PORT
    type: int
    required: false
    default: "3000"
  - name: CONTAINER_PORT
    type: int
    required: false
    default: "8080"
  - name: OPENAI_API_BASE_URL
    type: string
    required: false
    default: ""
  - name: OPENAI_API_KEY
    type: string
    required: false
    default: ""
  - name: WEBUI_SECRET_KEY
    type: string
    required: false
    default: ""
access:
  - "Web 界面: http://<fortos-ip>:3000"
  - "对接 Ollama: 部署 Ollama 后,在 WebUI 设置中填入 Ollama API 地址 http://<fortos-ip>:11434"
  - "对接 OpenAI 兼容 API: 设置 OPENAI_API_BASE_URL 与 OPENAI_API_KEY 后重启"
  - "文档: https://docs.openwebui.com"
compose:
  services:
    "{{.AgentId}}":
      image: "{{.ImageName}}"
      restart: unless-stopped
      ports:
        - "${HOST_PORT}:${CONTAINER_PORT}"
      environment:
        OPENAI_API_BASE_URL: "${OPENAI_API_BASE_URL}"
        OPENAI_API_KEY: "${OPENAI_API_KEY}"
        WEBUI_SECRET_KEY: "${WEBUI_SECRET_KEY}"
""",
        ["lobe-chat"] = """
id: lobe-chat
name: LobeChat
logo: /logos/lobe-chat.ico
version: 1.0.0
description: LobeChat — 现代化 AI 聊天框架,多模型提供商(OpenAI / Anthropic / Google / Ollama),支持插件与知识库。
capabilities_required:
  - storage:share:media:read
parameters:
  - name: image
    type: string
    required: false
    default: docker.io/lobehub/lobe-chat:latest
  - name: HOST_PORT
    type: int
    required: false
    default: "3210"
  - name: CONTAINER_PORT
    type: int
    required: false
    default: "3210"
  - name: OPENAI_API_KEY
    type: string
    required: false
    default: ""
  - name: ACCESS_CODE
    type: string
    required: false
    default: ""
access:
  - "Web 界面: http://<fortos-ip>:3210"
  - "访问口令: 设置 ACCESS_CODE 后需输入口令才能进入"
  - "OpenAI 兼容 API: 在设置中填入 OPENAI_API_KEY,或配置 OPENAI_PROXY_URL 指向代理"
  - "文档: https://lobehub.com/docs"
compose:
  services:
    "{{.AgentId}}":
      image: "{{.ImageName}}"
      restart: unless-stopped
      ports:
        - "${HOST_PORT}:${CONTAINER_PORT}"
      environment:
        OPENAI_API_KEY: "${OPENAI_API_KEY}"
        ACCESS_CODE: "${ACCESS_CODE}"
""",
        ["n8n"] = """
id: n8n
name: n8n
logo: /logos/n8n.ico
version: 1.0.0
description: n8n — 工作流自动化平台,支持 400+ 集成,可接入 Telegram / Slack / Webhook 构建聊天机器人。
capabilities_required:
  - storage:share:media:read
parameters:
  - name: image
    type: string
    required: false
    default: docker.n8n.io/n8nio/n8n:latest
  - name: data_dir
    type: string
    required: false
    default: /home/node/.n8n
  - name: HOST_PORT
    type: int
    required: false
    default: "5678"
  - name: CONTAINER_PORT
    type: int
    required: false
    default: "5678"
  - name: GENERIC_TIMEZONE
    type: string
    required: false
    default: Asia/Shanghai
  - name: N8N_SECURE_COOKIE
    type: string
    required: false
    default: "false"
access:
  - "Web 界面: http://<fortos-ip>:5678"
  - "Telegram 接入: 在 n8n 中新建 Telegram Trigger 节点并粘贴 BotFather token"
  - "Webhook 接入: 工作流添加 Webhook 节点,地址为 http://<fortos-ip>:5678/webhook/<path>"
  - "文档: https://docs.n8n.io"
compose:
  services:
    "{{.AgentId}}":
      image: "{{.ImageName}}"
      restart: unless-stopped
      ports:
        - "${HOST_PORT}:${CONTAINER_PORT}"
      environment:
        GENERIC_TIMEZONE: "${GENERIC_TIMEZONE}"
        N8N_SECURE_COOKIE: "${N8N_SECURE_COOKIE}"
""",
        ["anythingllm"] = """
id: anythingllm
name: AnythingLLM
logo: /logos/anythingllm.svg
version: 1.0.0
description: AnythingLLM — 全栈 LLM 应用,内置知识库(RAG),可对接多种模型与工作区,适合团队知识问答。
capabilities_required:
  - storage:share:media:read
parameters:
  - name: image
    type: string
    required: false
    default: mintplexlabs/anythingllm:latest
  - name: data_dir
    type: string
    required: false
    default: /app/server/storage
  - name: HOST_PORT
    type: int
    required: false
    default: "3001"
  - name: CONTAINER_PORT
    type: int
    required: false
    default: "3001"
  - name: STORAGE_DIR
    type: string
    required: false
    default: /app/server/storage
  - name: SERVER_PORT
    type: int
    required: false
    default: "3001"
access:
  - "Web 界面: http://<fortos-ip>:3001"
  - "首次访问创建管理员账号"
  - "模型接入: 设置页选择 OpenAI / Ollama / 本地模型等并填入 API Key"
  - "文档: https://docs.anythingllm.com"
compose:
  services:
    "{{.AgentId}}":
      image: "{{.ImageName}}"
      restart: unless-stopped
      ports:
        - "${HOST_PORT}:${CONTAINER_PORT}"
      environment:
        STORAGE_DIR: "${STORAGE_DIR}"
        SERVER_PORT: "${SERVER_PORT}"
""",
        ["ollama"] = """
id: ollama
name: Ollama
logo: /logos/ollama.png
version: 1.0.0
description: Ollama — 本地大模型运行时,一条命令运行 Llama / Qwen / DeepSeek 等开源模型,提供 OpenAI 兼容 API。
capabilities_required:
  - storage:share:media:read
parameters:
  - name: image
    type: string
    required: false
    default: ollama/ollama:latest
  - name: data_dir
    type: string
    required: false
    default: /root/.ollama
  - name: HOST_PORT
    type: int
    required: false
    default: "11434"
  - name: CONTAINER_PORT
    type: int
    required: false
    default: "11434"
  - name: OLLAMA_HOST
    type: string
    required: false
    default: 0.0.0.0
  - name: OLLAMA_MODELS
    type: string
    required: false
    default: /root/.ollama/models
access:
  - "API 地址: http://<fortos-ip>:11434 (OpenAI 兼容: /v1)"
  - "拉取模型: ssh 到宿主执行 docker exec <agent> ollama pull qwen2.5:7b"
  - "对接 Open WebUI / LobeChat: 填入 API 地址 http://<fortos-ip>:11434"
  - "文档: https://ollama.com/library"
compose:
  services:
    "{{.AgentId}}":
      image: "{{.ImageName}}"
      restart: unless-stopped
      ports:
        - "${HOST_PORT}:${CONTAINER_PORT}"
      environment:
        OLLAMA_HOST: "${OLLAMA_HOST}"
        OLLAMA_MODELS: "${OLLAMA_MODELS}"
""",
        ["langflow"] = """
id: langflow
name: Langflow
logo: /logos/langflow.ico
version: 1.0.0
description: Langflow — 可视化 LLM 工作流搭建平台(拖拽式 Agent / RAG / 多模型编排),支持导出 API。
capabilities_required:
  - storage:share:media:read
parameters:
  - name: image
    type: string
    required: false
    default: langflowai/langflow:latest
  - name: data_dir
    type: string
    required: false
    default: /app/langflow
  - name: HOST_PORT
    type: int
    required: false
    default: "7860"
  - name: CONTAINER_PORT
    type: int
    required: false
    default: "7860"
access:
  - "Web 界面: http://<fortos-ip>:7860"
  - "首次访问创建管理员账号"
  - "API 接入: 在项目中开启 API,获得 /api/v1/run/<flow-id> 端点,可对接外部聊天工具"
  - "文档: https://docs.langflow.org"
compose:
  services:
    "{{.AgentId}}":
      image: "{{.ImageName}}"
      restart: unless-stopped
      ports:
        - "${HOST_PORT}:${CONTAINER_PORT}"
""",
        ["opencode"] = """
id: opencode
name: OpenCode
logo: /logos/opencode.svg
version: 1.0.0
description: OpenCode — 开源 AI 编程/运维 Agent(终端原生,24h 常驻),可连接 OpenAI 兼容端点(含本地 Ollama)。适合在 NAS 上做 AI 宿主机:手机 SSH 进容器即可指挥。
capabilities_required:
  - storage:share:media:read
parameters:
  - name: image
    type: string
    required: false
    default: ghcr.io/sst/opencode:latest
  - name: data_dir
    type: string
    required: false
    default: /root/.local/share/opencode
  - name: HOST_PORT
    type: int
    required: false
    default: "18790"
  - name: CONTAINER_PORT
    type: int
    required: false
    default: "18790"
  - name: OPENAI_API_KEY
    type: string
    required: false
    default: ""
  - name: OPENAI_BASE_URL
    type: string
    required: false
    default: http://host.docker.internal:11434/v1
  - name: OPENAI_MODEL
    type: string
    required: false
    default: qwen2.5:7b
access:
  - "终端交互: ssh 到宿主后 docker exec -it <agent> opencode"
  - "手机指挥: 部署后开启 SSH(见 fortOS 网络页),手机终端进入容器即可用自然语言驱动 opencode"
  - "对接本地 Ollama: 默认 OPENAI_BASE_URL 指向宿主 11434(Ollama),无需外网 API Key"
  - "对接外部模型: 修改 OPENAI_API_KEY / OPENAI_BASE_URL / OPENAI_MODEL 后重启"
  - "文档: https://opencode.ai"
compose:
  services:
    "{{.AgentId}}":
      image: "{{.ImageName}}"
      restart: unless-stopped
      ports:
        - "${HOST_PORT}:${CONTAINER_PORT}"
      environment:
        OPENAI_API_KEY: "${OPENAI_API_KEY}"
        OPENAI_BASE_URL: "${OPENAI_BASE_URL}"
        OPENAI_MODEL: "${OPENAI_MODEL}"
      extra_hosts:
        - "host.docker.internal:host-gateway"
""",
        ["hermes"] = """
id: hermes
name: Hermes Agent
logo: /logos/hermes.svg
version: 1.0.0
description: Hermes — 轻量常驻 AI 助手(OpenAI 兼容),面向"24 小时运行、手机指挥"场景:常驻监听,任务/问答经 API 或终端发起。适合与 OpenCode 配合做个人 AI 宿主。
capabilities_required:
  - storage:share:media:read
parameters:
  - name: image
    type: string
    required: false
    default: ghcr.io/anthropic-ai/hermes:latest
  - name: data_dir
    type: string
    required: false
    default: /data
  - name: HOST_PORT
    type: int
    required: false
    default: "18791"
  - name: CONTAINER_PORT
    type: int
    required: false
    default: "18791"
  - name: OPENAI_API_KEY
    type: string
    required: false
    default: ""
  - name: OPENAI_BASE_URL
    type: string
    required: false
    default: http://host.docker.internal:11434/v1
  - name: OPENAI_MODEL
    type: string
    required: false
    default: qwen2.5:7b
  - name: HERMES_WORKSPACE
    type: string
    required: false
    default: /data/workspace
access:
  - "API 地址: http://<fortos-ip>:18791 (OpenAI 兼容 chat/completions)"
  - "手机指挥: 任何支持 OpenAI 兼容客户端的 App/脚本把 base URL 指向该地址即可对话"
  - "对接本地 Ollama: 默认 OPENAI_BASE_URL 指向宿主 11434,无需外网 Key"
  - "文档: https://hermes.example.ai"
compose:
  services:
    "{{.AgentId}}":
      image: "{{.ImageName}}"
      restart: unless-stopped
      ports:
        - "${HOST_PORT}:${CONTAINER_PORT}"
      environment:
        OPENAI_API_KEY: "${OPENAI_API_KEY}"
        OPENAI_BASE_URL: "${OPENAI_BASE_URL}"
        OPENAI_MODEL: "${OPENAI_MODEL}"
        HERMES_WORKSPACE: "${HERMES_WORKSPACE}"
      extra_hosts:
        - "host.docker.internal:host-gateway"
""",
        ["jellyfin"] = """
id: jellyfin
name: Jellyfin
logo: /logos/jellyfin.svg
version: 1.0.0
description: Jellyfin — 开源影音媒体中心(免费 Plex 替代),支持 H.265/HEVC 硬件转码直通(Intel/AMD /dev/dri)。可管理影视库并串流到手机/电视。
capabilities_required:
  - storage:share:media:read
parameters:
  - name: image
    type: string
    required: false
    default: jellyfin/jellyfin:latest
  - name: data_dir
    type: string
    required: false
    default: /config
  - name: media_dir
    type: string
    required: false
    default: /media
  - name: HOST_PORT
    type: int
    required: false
    default: "8096"
  - name: CONTAINER_PORT
    type: int
    required: false
    default: "8096"
  - name: TZ
    type: string
    required: false
    default: UTC
access:
  - "Web 界面: http://<fortos-ip>:8096"
  - "首次访问设置管理员账号与媒体库"
  - "硬件转码: 部署时挂载 /dev/dri(Intel/AMD iGPU),Jellyfin 转码设置选择 QSV/VAAPI 即支持 H.265/HEVC"
  - "手机端: Jellyfin 官方 App 连接 http://<fortos-ip>:8096,可配合 P0-3 远程访问在户外观看"
  - "文档: https://jellyfin.org/docs"
compose:
  services:
    "{{.AgentId}}":
      image: "{{.ImageName}}"
      restart: unless-stopped
      ports:
        - "${HOST_PORT}:${CONTAINER_PORT}"
      devices:
        - /dev/dri:/dev/dri
      environment:
        TZ: "${TZ}"
""",
        ["kodi"] = """
id: kodi
name: Kodi (家庭 KTV / 影院)
logo: /logos/kodi.svg
version: 1.0.0
description: Kodi — 开源家庭影院/媒体中心,配合点歌插件可做家庭 KTV。桌面/手机/电视多端客户端访问同一个媒体库。
capabilities_required:
  - storage:share:media:read
parameters:
  - name: image
    type: string
    required: false
    default: docker.io/linuxserver/kodi:latest
  - name: data_dir
    type: string
    required: false
    default: /config
  - name: media_dir
    type: string
    required: false
    default: /media
  - name: PUID
    type: int
    required: false
    default: "1000"
  - name: PGID
    type: int
    required: false
    default: "1000"
  - name: TZ
    type: string
    required: false
    default: Asia/Shanghai
access:
  - "说明: 家庭 KTV 需要电视/显示器 + Kodi 客户端(Kodi 官方 App,全平台含 AppleTV)。NAS 上部署本服务托管媒体库与点歌插件。"
  - "Web 远程控制: 安装 Kodi web 界面(默认 8080 端口)后可从手机浏览器遥控。"
  - "点歌插件: 在 Kodi 内安装 KTV 点歌插件(如「酷我音乐」类 VOD 插件),AppleTV 端建议配合 Jellyfin 做媒体播放。"
  - "文档: https://kodi.tv"
compose:
  services:
    "{{.AgentId}}":
      image: "{{.ImageName}}"
      restart: unless-stopped
      ports:
        - "8080:8080"
      environment:
        PUID: "${PUID}"
        PGID: "${PGID}"
        TZ: "${TZ}"
      devices:
        - /dev/dri:/dev/dri
""",
    };
    private readonly HttpClient _httpClient;
    private readonly IDeserializer _deserializer;
    private readonly ISerializer _serializer;
    private readonly SemaphoreSlim _seedLock = new(1, 1);
    private int _seeded;

    /// <summary>
    /// Initialize the Agent template catalog.
    /// </summary>
    /// <param name="httpClient">Optional HTTP client.</param>
    public AgentCatalog(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _deserializer = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build();
        _serializer = new SerializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentTemplate>> ListTemplatesAsync(CancellationToken ct)
    {
        await EnsureBuiltInTemplatesAsync(ct).ConfigureAwait(false);
        if (!Directory.Exists(AgentPaths.CatalogRoot))
        {
            return [];
        }

        var templates = new List<AgentTemplate>();
        foreach (var path in Directory.EnumerateFiles(AgentPaths.CatalogRoot, "*.template.yaml").Order(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            templates.Add(await LoadTemplateAsync(path, ct).ConfigureAwait(false));
        }

        return templates;
    }

    /// <inheritdoc />
    public async Task<AgentTemplate?> GetTemplateAsync(string templateId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        await EnsureBuiltInTemplatesAsync(ct).ConfigureAwait(false);
        var path = Path.Combine(AgentPaths.CatalogRoot, templateId + ".template.yaml");
        return File.Exists(path) ? await LoadTemplateAsync(path, ct).ConfigureAwait(false) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentTemplate>> SearchTemplatesAsync(string query, CancellationToken ct)
    {
        await EnsureBuiltInTemplatesAsync(ct).ConfigureAwait(false);
        var needle = query ?? string.Empty;
        var templates = await ListTemplatesAsync(ct).ConfigureAwait(false);
        return [.. templates.Where(t => Contains(t.Id, needle) || Contains(t.Name, needle) || Contains(t.Description, needle))];
    }

    /// <inheritdoc />
    public async Task<AgentTemplate> InstallTemplateAsync(string source, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        await EnsureBuiltInTemplatesAsync(ct).ConfigureAwait(false);
        var yaml = await ReadSourceAsync(source, ct).ConfigureAwait(false);
        var template = ParseAndValidate(yaml, source);
        Directory.CreateDirectory(AgentPaths.CatalogRoot);
        var destination = Path.Combine(AgentPaths.CatalogRoot, template.Id + ".template.yaml");
        await File.WriteAllTextAsync(destination, yaml, ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(GetSourcePath(template.Id), source, ct).ConfigureAwait(false);
        return template;
    }

    /// <inheritdoc />
    public async Task<AgentTemplate> UpdateTemplateAsync(string templateId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        await EnsureBuiltInTemplatesAsync(ct).ConfigureAwait(false);
        var sourcePath = GetSourcePath(templateId);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Template {templateId} has no updatable source record.", sourcePath);
        }

        var source = (await File.ReadAllTextAsync(sourcePath, ct).ConfigureAwait(false)).Trim();

        // Validate the template (including identifier match) BEFORE writing anything:
        // InstallTemplateAsync overwrites both the .template.yaml and .source files, so an
        // id mismatch discovered afterwards would leave the catalog in a dirty state with no
        // way to roll back.
        var template = ParseAndValidate(source, sourcePath);
        if (!string.Equals(template.Id, templateId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Source template identifier {template.Id} does not match the requested identifier {templateId}.");
        }

        return await InstallTemplateAsync(source, ct).ConfigureAwait(false);
    }

    private async Task<AgentTemplate> LoadTemplateAsync(string path, CancellationToken ct)
    {
        var yaml = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        return ParseAndValidate(yaml, path);
    }

    private async Task<string> ReadSourceAsync(string source, CancellationToken ct)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "http" or "https")
            {
                return await _httpClient.GetStringAsync(uri, ct).ConfigureAwait(false);
            }

            if (uri.Scheme != Uri.UriSchemeFile)
            {
                throw new NotSupportedException($"Unsupported template source protocol: {uri.Scheme}.");
            }
        }

        var path = uri?.Scheme == Uri.UriSchemeFile ? uri.LocalPath : source;
        // Local source whitelist: only files inside the catalog directory are allowed, preventing file://
        // from reading arbitrary host files (e.g. /etc/shadow); their content would be parsed and persisted into the catalog — arbitrary file read.
        if (!PathSafety.IsPathUnderRoot(Path.GetFullPath(path), AgentPaths.CatalogRoot))
        {
            throw new ArgumentException("Local template source must be located within the agent catalog directory.", nameof(source));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Template source file does not exist.", path);
        }

        return await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
    }

    private AgentTemplate ParseAndValidate(string yaml, string sourceName)
    {
        try
        {
            var dto = _deserializer.Deserialize<TemplateDto>(yaml) ?? throw new InvalidOperationException("Template is empty.");
            var compose = _serializer.Serialize(dto.Compose ?? throw new InvalidOperationException("Template missing compose section."));
            var template = new AgentTemplate
            {
                Id = dto.Id ?? string.Empty,
                Name = dto.Name ?? string.Empty,
                Version = dto.Version ?? string.Empty,
                Description = dto.Description,
                Logo = dto.Logo,
                CapabilitiesRequired = dto.CapabilitiesRequired ?? [],
                Parameters = dto.Parameters?.Select(static p => new AgentTemplateParameter
                {
                    Name = p.Name ?? string.Empty,
                    Type = p.Type ?? string.Empty,
                    Required = p.Required,
                    Default = p.Default,
                }).ToArray() ?? [],
                AccessNotes = dto.Access ?? [],
                ComposeTemplate = compose,
            };

            Validate(template);
            return template;
        }
        catch (YamlException ex)
        {
            throw new InvalidDataException($"Template YAML parsing failed: {sourceName}.", ex);
        }
    }

    private static void Validate(AgentTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.Id) || string.IsNullOrWhiteSpace(template.Name) || string.IsNullOrWhiteSpace(template.Version) || string.IsNullOrWhiteSpace(template.ComposeTemplate))
        {
            throw new InvalidDataException("Template is missing required fields: id, name, version, or compose.");
        }

        if (!IdPattern.IsMatch(template.Id))
        {
            throw new InvalidDataException("Template id must match ^[a-z][a-z0-9-]{1,63}$.");
        }

        if (!System.Version.TryParse(template.Version, out _))
        {
            throw new InvalidDataException("Template version must be a parseable version number.");
        }

        foreach (var parameter in template.Parameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Name) || string.IsNullOrWhiteSpace(parameter.Type))
            {
                throw new InvalidDataException("Template parameters must include name and type.");
            }
        }
    }

    private static bool Contains(string? value, string query) => value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

    private static string GetSourcePath(string templateId) => Path.Combine(AgentPaths.CatalogRoot, templateId + ".source");

    /// <summary>
    /// Automatically writes minimal built-in templates on first access to the
    /// catalog to avoid empty-directory deployment failures.
    /// </summary>
    private async Task EnsureBuiltInTemplatesAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _seeded) == 1)
        {
            return;
        }

        await _seedLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_seeded == 1)
            {
                return;
            }

            Directory.CreateDirectory(AgentPaths.CatalogRoot);
            // Seed any missing built-in templates individually so new templates ship to
            // existing installations without overwriting user-installed ones.
            foreach (var pair in BuiltInTemplates)
            {
                var destination = Path.Combine(AgentPaths.CatalogRoot, pair.Key + ".template.yaml");
                if (!File.Exists(destination))
                {
                    await File.WriteAllTextAsync(destination, pair.Value, ct).ConfigureAwait(false);
                }
            }

            _seeded = 1;
        }
        finally
        {
            _seedLock.Release();
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex AgentIdRegex();

    private sealed class TemplateDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? Description { get; set; }
        public string? Logo { get; set; }
        public string[]? CapabilitiesRequired { get; set; }
        public ParameterDto[]? Parameters { get; set; }
        public string[]? Access { get; set; }
        public object? Compose { get; set; }
    }

    private sealed class ParameterDto
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public bool Required { get; set; }
        public string? Default { get; set; }
    }
}
