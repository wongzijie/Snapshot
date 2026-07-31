using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Snapshot;

public sealed record TranslationResult(
    string Text,
    string TargetLanguage,
    string? DetectedSourceLanguage);

public sealed record TranslatedTextRegion(
    string SourceText,
    string TranslatedText,
    System.Drawing.RectangleF Bounds);

public sealed record RegionTranslationResult(
    IReadOnlyList<TranslatedTextRegion> Regions,
    string Text,
    string TargetLanguage,
    string? DetectedSourceLanguage);

public sealed class TranslatorService
{
    private const int MaximumItemsPerRequest = 25;
    private const int MaximumCharactersPerRequest = 4800;
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(25)
    };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<TranslationResult> TranslateAsync(
        string text,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new TranslationResult(
                string.Empty,
                settings.TranslationTargetLanguage,
                null);
        }

        var endpoint = ValidateSettings(settings);
        var chunks = SplitText(text, MaximumCharactersPerRequest);
        var translated = new StringBuilder();
        string? detectedLanguage = null;
        foreach (var chunk in chunks)
        {
            var items = await SendBatchAsync(
                [chunk],
                settings,
                endpoint,
                cancellationToken);
            var item = items[0];
            if (translated.Length > 0)
            {
                translated.AppendLine();
            }

            translated.Append(item.Text);
            detectedLanguage ??= item.DetectedSourceLanguage;
        }

        return new TranslationResult(
            translated.ToString(),
            settings.TranslationTargetLanguage,
            detectedLanguage);
    }

    public async Task<RegionTranslationResult> TranslateRegionsAsync(
        IReadOnlyList<OcrTextRegion> regions,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        var endpoint = ValidateSettings(settings);
        var usableRegions = regions
            .Where(region => !string.IsNullOrWhiteSpace(region.Text))
            .ToArray();
        if (usableRegions.Length == 0)
        {
            return new RegionTranslationResult(
                [],
                string.Empty,
                settings.TranslationTargetLanguage,
                null);
        }

        var translatedByIndex = new string[usableRegions.Length];
        string? detectedLanguage = null;
        var batchIndexes = new List<int>(MaximumItemsPerRequest);
        var batchTexts = new List<string>(MaximumItemsPerRequest);
        var batchCharacters = 0;

        async Task FlushBatchAsync()
        {
            if (batchTexts.Count == 0)
            {
                return;
            }

            var items = await SendBatchAsync(
                batchTexts,
                settings,
                endpoint,
                cancellationToken);
            for (var index = 0; index < items.Count; index++)
            {
                translatedByIndex[batchIndexes[index]] = items[index].Text;
                detectedLanguage ??= items[index].DetectedSourceLanguage;
            }

            batchIndexes.Clear();
            batchTexts.Clear();
            batchCharacters = 0;
        }

        for (var index = 0; index < usableRegions.Length; index++)
        {
            var text = usableRegions[index].Text;
            if (text.Length > MaximumCharactersPerRequest)
            {
                await FlushBatchAsync();
                var longTranslation = await TranslateAsync(
                    text,
                    settings,
                    cancellationToken);
                translatedByIndex[index] = longTranslation.Text;
                detectedLanguage ??= longTranslation.DetectedSourceLanguage;
                continue;
            }

            if (batchTexts.Count >= MaximumItemsPerRequest ||
                batchCharacters + text.Length > MaximumCharactersPerRequest)
            {
                await FlushBatchAsync();
            }

            batchIndexes.Add(index);
            batchTexts.Add(text);
            batchCharacters += text.Length;
        }

        await FlushBatchAsync();

        var translatedRegions = usableRegions
            .Select((region, index) => new TranslatedTextRegion(
                region.Text,
                translatedByIndex[index],
                region.Bounds))
            .ToArray();
        return new RegionTranslationResult(
            translatedRegions,
            string.Join(Environment.NewLine, translatedRegions.Select(region => region.TranslatedText)),
            settings.TranslationTargetLanguage,
            detectedLanguage);
    }

    private static Uri ValidateSettings(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.TranslatorApiKey))
        {
            throw new TranslatorConfigurationException(
                "尚未配置 Microsoft Translator 密钥。请打开 Snapshot 设置，在“OCR 与图片翻译”中填写密钥；若 Azure 资源要求，再填写区域。");
        }

        if (!Uri.TryCreate(settings.TranslatorEndpoint, UriKind.Absolute, out var endpoint) ||
            !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new TranslatorConfigurationException(
                "翻译服务地址无效；请使用 HTTPS 地址。");
        }

        return endpoint;
    }

    private static async Task<IReadOnlyList<BatchTranslation>> SendBatchAsync(
        IReadOnlyList<string> texts,
        AppSettings settings,
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        var targetLanguage = Uri.EscapeDataString(settings.TranslationTargetLanguage);
        var requestUri = new Uri(
            endpoint,
            $"/translate?api-version=3.0&to={targetLanguage}");
        var json = JsonSerializer.Serialize(
            texts.Select(text => new TranslationRequest(text)));
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Ocp-Apim-Subscription-Key", settings.TranslatorApiKey);
        if (!string.IsNullOrWhiteSpace(settings.TranslatorRegion))
        {
            request.Headers.Add(
                "Ocp-Apim-Subscription-Region",
                settings.TranslatorRegion);
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = TryReadError(responseBody);
            throw new InvalidOperationException(
                $"翻译服务返回 {(int)response.StatusCode}：{detail}");
        }

        var result = JsonSerializer.Deserialize<List<TranslationResponse>>(
            responseBody,
            JsonOptions);
        if (result is null || result.Count != texts.Count)
        {
            throw new InvalidOperationException("翻译服务返回的分段数量与请求不一致。");
        }

        return result.Select(item =>
        {
            var translation = item.Translations?.FirstOrDefault()
                ?? throw new InvalidOperationException("翻译服务没有返回可用结果。");
            return new BatchTranslation(
                translation.Text ?? string.Empty,
                item.DetectedLanguage?.Language);
        }).ToArray();
    }

    private static IReadOnlyList<string> SplitText(string text, int maximumLength)
    {
        var chunks = new List<string>();
        var offset = 0;
        while (offset < text.Length)
        {
            var length = Math.Min(maximumLength, text.Length - offset);
            if (offset + length < text.Length)
            {
                var breakAt = text.LastIndexOfAny(
                    ['\r', '\n', '。', '.', '！', '!', '？', '?', ' '],
                    offset + length - 1,
                    length);
                if (breakAt >= offset + maximumLength / 2)
                {
                    length = breakAt - offset + 1;
                }
            }

            chunks.Add(text.Substring(offset, length));
            offset += length;
        }

        return chunks;
    }

    private static string TryReadError(string responseBody)
    {
        try
        {
            var error = JsonSerializer.Deserialize<TranslatorErrorEnvelope>(
                responseBody,
                JsonOptions);
            return string.IsNullOrWhiteSpace(error?.Error?.Message)
                ? "请检查密钥、区域和网络连接。"
                : error.Error.Message;
        }
        catch
        {
            return "请检查密钥、区域和网络连接。";
        }
    }

    private sealed record TranslationRequest(string Text);

    private sealed record BatchTranslation(
        string Text,
        string? DetectedSourceLanguage);

    private sealed class TranslationResponse
    {
        public DetectedLanguageResponse? DetectedLanguage { get; set; }
        public List<TranslationItem>? Translations { get; set; }
    }

    private sealed class DetectedLanguageResponse
    {
        public string? Language { get; set; }
    }

    private sealed class TranslationItem
    {
        public string? Text { get; set; }
    }

    private sealed class TranslatorErrorEnvelope
    {
        public TranslatorError? Error { get; set; }
    }

    private sealed class TranslatorError
    {
        public string? Message { get; set; }
    }
}

public sealed class TranslatorConfigurationException(string message)
    : InvalidOperationException(message);
