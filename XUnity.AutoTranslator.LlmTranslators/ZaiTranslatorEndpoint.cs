using SimpleJSON;
using System.Net;
using System.Text;
using XUnity.AutoTranslator.LlmTranslators.Config;
using XUnity.AutoTranslator.Plugin.Core.Endpoints;
using XUnity.AutoTranslator.Plugin.Core.Endpoints.Http;
using XUnity.AutoTranslator.Plugin.Core.Web;

public class ZaiTranslatorEndpoint : HttpEndpoint
{
    public override string Id => "ZaiTranslate";
    public override string FriendlyName => "Zai Translate (Zhipu AI GLM)";
    public override int MaxTranslationsPerRequest => 1;
    public override int MaxConcurrency => 15;

    private LlmConfig _config = new();

    public override void Initialize(IInitializationContext context)
    {
        string folder = Configuration.CalculateConfigFolder();
        var file = Path.Combine(folder, "Zai.yaml");
        _config = Configuration.GetConfiguration(file);
        Configuration.LoadGlossary(_config, "Zai-Glossary.yaml");

        // Remove artificial delays
        context.SetTranslationDelay(0.1f);
        context.DisableSpamChecks();

        if (string.IsNullOrEmpty(_config.ApiKey))
            throw new Exception("Z.ai endpoint requires an API key. Please configure it in Zai.yaml");
    }

    public override void OnCreateRequest(IHttpRequestCreationContext context)
    {
        var requestData = GetZaiRequestData(_config, context.UntranslatedText);

        var request = new XUnityWebRequest("POST", GetEndpointUrl(_config), requestData);
        request.Headers[HttpRequestHeader.Authorization] = $"Bearer {_config.ApiKey}";
        request.Headers[HttpRequestHeader.ContentType] = "application/json";

        // Add Accept-Language header as per Z.ai API specification
        request.Headers["Accept-Language"] = "en-US,en";

        context.Complete(request);
    }

    private string GetEndpointUrl(LlmConfig config)
    {
        return config.UseCodingEndpoint
            ? "https://api.z.ai/api/coding/paas/v4/chat/completions"
            : config.Url;
    }

    public override void OnExtractTranslation(IHttpTranslationExtractionContext context)
    {
        var data = context.Response.Data;

        var jsonResponse = JSON.Parse(data);
        var result = jsonResponse["choices"]?[0]?["message"]?["content"]?.ToString() ?? string.Empty;
        result = ValidateAndCleanupTranslation(context.UntranslatedText, result);

        if (MaxTranslationsPerRequest == 1)
            context.Complete(result);
    }

    private string GetZaiRequestData(LlmConfig config, string raw)
    {
        var systemPrompt = new StringBuilder(config.SystemPrompt);
        systemPrompt.AppendLine(ConstructGlossaryPrompt(raw, config));

        // Create messages array
        var messages = new JSONArray();
        var systemMessage = new JSONObject();
        systemMessage["role"] = "system";
        systemMessage["content"] = systemPrompt.ToString();
        messages.Add(systemMessage);

        var userMessage = new JSONObject();
        userMessage["role"] = "user";
        userMessage["content"] = raw;
        messages.Add(userMessage);

        // Create request body according to Z.ai API specification
        var requestBody = new JSONObject
        {
            ["model"] = config.Model,
            ["stream"] = false,
            ["messages"] = messages
        };

        // Add model parameters with Z.ai supported values
        if (config.ModelParams != null)
        {
            foreach (var param in config.ModelParams)
            {
                if (decimal.TryParse(param.Value.ToString(), out decimal isDecimal))
                    requestBody[param.Key] = (double)isDecimal;
                else if (int.TryParse(param.Value.ToString(), out int isInt))
                    requestBody[param.Key] = isInt;
                else if (bool.TryParse(param.Value.ToString(), out bool isBool))
                    requestBody[param.Key] = isBool;
                else
                    requestBody[param.Key] = param.Value.ToString();
            }
        }
        else
        {
            // Z.ai default values
            requestBody["temperature"] = 0.6;
            requestBody["top_p"] = 0.95;
            requestBody["max_tokens"] = 4096;
        }

        return requestBody.ToString();
    }

    private string ConstructGlossaryPrompt(string raw, LlmConfig config)
    {
        var prompt = new StringBuilder(config.GlossaryPrompt);
        foreach (var line in config.GlossaryLines)
        {
            if (raw.Contains(line.Raw))
                prompt.Append($"- {line.Raw}: {line.Result}\n");
        }
        return prompt.ToString();
    }

    private string ValidateAndCleanupTranslation(string raw, string result)
    {
        if (string.IsNullOrEmpty(result))
            return result;

        // Remove surrounding quotes
        if ((result.StartsWith("\"") && result.EndsWith("\""))
            || (result.StartsWith("'") && result.EndsWith("'")))
            result = result.Substring(1, result.Length - 2);

        // Convert wide quotes to standard quotes
        result = result
            .Replace("'", "'")
            .Replace("'", "'");

        // Unescape special characters
        result = System.Text.RegularExpressions.Regex.Unescape(result);

        // Ensure first character is uppercase (if applicable)
        if (result.Length > 0 && char.IsLower(result[0]) && raw != result)
            result = char.ToUpper(result[0]) + result.Substring(1);

        return result.Trim();
    }
}
