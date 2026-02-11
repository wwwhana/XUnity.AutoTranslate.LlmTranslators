using SimpleJSON;
using System.Net;
using XUnity.AutoTranslator.LlmTranslators.Behavior;
using XUnity.AutoTranslator.LlmTranslators.Config;
using XUnity.AutoTranslator.Plugin.Core.Endpoints;
using XUnity.AutoTranslator.Plugin.Core.Endpoints.Http;
using XUnity.AutoTranslator.Plugin.Core.Web;

public class OllamaTranslatorEndpoint : HttpEndpoint
{
    public override string Id => "OllamaTranslate";
    public override string FriendlyName => "Ollama Translate";
    public override int MaxTranslationsPerRequest => 1;

    public override int MaxConcurrency => _config.MaxConcurrency;

    private LlmConfig _config = new();

    public override void Initialize(IInitializationContext context)
    {
        string folder = Configuration.CalculateConfigFolder();
        var file = Path.Combine(folder, "Ollama.yaml");
        _config = Configuration.GetConfiguration(file);
        Configuration.LoadGlossary(_config, "Ollama-Glossary.yaml");

        context.SetTranslationDelay(_config.TranslationDelay);
        context.DisableSpamChecks();

        if (string.IsNullOrEmpty(_config.ApiKey) && _config.ApiKeyRequired)
            throw new Exception("The endpoint requires an API key which has not been provided.");
    }

    public override void OnCreateRequest(IHttpRequestCreationContext context)
    {
        var requestData = BaseEndpointBehavior.GetRequestData(_config, context.UntranslatedText);

        var request = new XUnityWebRequest("POST", _config.Url, requestData);
        request.Headers[HttpRequestHeader.ContentType] = "application/json";

        if (_config.ApiKeyRequired)
            request.Headers[HttpRequestHeader.Authorization] = $"Bearer {_config.ApiKey}";

        context.Complete(request);
    }    

    public override void OnExtractTranslation(IHttpTranslationExtractionContext context)
    {
        var data = context.Response.Data;

        var jsonResponse = JSON.Parse(data);
        var result = jsonResponse["message"]?["content"]?.ToString() ?? string.Empty;
        result = BaseEndpointBehavior.ValidateAndCleanupTranslation(context.UntranslatedText, result, _config);

        if (MaxTranslationsPerRequest == 1)
            context.Complete(result);
    }
}