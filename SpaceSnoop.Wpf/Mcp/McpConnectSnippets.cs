namespace SpaceSnoop.Wpf.Mcp;

public static class McpConnectSnippets
{
    public static string For(int formatIndex, string url, string token)
    {
        return formatIndex switch
        {
            1 => Claude(url, token),
            2 => Codex(url, token),
            3 => OpenCode(url, token),
            4 => Endpoint(url, token),
            _ => Json(url, token),
        };
    }

    private static string Json(string url, string token)
    {
        return $$"""
                 {
                   "mcpServers": {
                     "{{AgentPrompt.ServerName}}": {
                       "type": "http",
                       "url": "{{url}}",
                       "headers": {
                         "Authorization": "Bearer {{token}}"
                       }
                     }
                   }
                 }
                 """;
    }

    private static string Claude(string url, string token)
    {
        return $"claude mcp add --transport http {AgentPrompt.ServerName} {url} --header \"Authorization: Bearer {token}\"";
    }

    private static string Codex(string url, string token)
    {
        return $$"""
                 # Токен – в переменную окружения: {{AgentBackendBase.TokenVariable}}={{token}}
                 [mcp_servers.{{AgentPrompt.ServerName}}]
                 url = "{{url}}"
                 bearer_token_env_var = "{{AgentBackendBase.TokenVariable}}"
                 """;
    }

    private static string OpenCode(string url, string token)
    {
        return $$"""
                 {
                   "mcp": {
                     "{{AgentPrompt.ServerName}}": {
                       "type": "remote",
                       "enabled": true,
                       "url": "{{url}}",
                       "headers": {
                         "Authorization": "Bearer {{token}}"
                       }
                     }
                   }
                 }
                 """;
    }

    private static string Endpoint(string url, string token)
    {
        return $"Адрес: {url}{Environment.NewLine}Транспорт: Streamable HTTP{Environment.NewLine}Заголовок: Authorization: Bearer {token}";
    }
}
