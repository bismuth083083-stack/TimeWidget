using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TimeWidget.Models;

namespace TimeWidget.Services;

public sealed class OpenAiService
{
    private const string SystemPrompt =
        "Answer concisely and clearly. If the question needs current information, say that browsing is not available in this widget.";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task<string> AskAsync(string question, AiSearchSettings settings)
    {
        string? apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return "OPENAI_API_KEY is not set";
        }

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, "https://api.openai.com/v1/responses");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            object payload = new
            {
                model = string.IsNullOrWhiteSpace(settings.ModelName) ? "gpt-4.1-mini" : settings.ModelName,
                instructions = SystemPrompt,
                input = question,
                max_output_tokens = settings.MaxOutputTokens > 0 ? settings.MaxOutputTokens : 500
            };

            string json = JsonSerializer.Serialize(payload);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await HttpClient.SendAsync(request).ConfigureAwait(false);
            string responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return $"OpenAI request failed ({(int)response.StatusCode}).";
            }

            return ExtractResponseText(responseJson);
        }
        catch (TaskCanceledException)
        {
            return "Request timed out. Please try again.";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OpenAI request failed: {ex.Message}");
            return "Unable to ask OpenAI right now.";
        }
    }

    private static string ExtractResponseText(string responseJson)
    {
        JsonNode? root = JsonNode.Parse(responseJson);
        string? outputText = root?["output_text"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(outputText))
        {
            return outputText.Trim();
        }

        JsonArray? output = root?["output"]?.AsArray();
        if (output is null)
        {
            return "No answer returned.";
        }

        StringBuilder builder = new();
        foreach (JsonNode? outputItem in output)
        {
            JsonArray? content = outputItem?["content"]?.AsArray();
            if (content is null)
            {
                continue;
            }

            foreach (JsonNode? contentItem in content)
            {
                string? text = contentItem?["text"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    builder.AppendLine(text);
                }
            }
        }

        string answer = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(answer) ? "No answer returned." : answer;
    }
}
