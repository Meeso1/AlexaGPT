using Microsoft.AspNetCore.Mvc;
using OpenAI.GPT3;
using OpenAI.GPT3.Interfaces;
using OpenAI.GPT3.Managers;
using OpenAI.GPT3.ObjectModels;
using OpenAI.GPT3.ObjectModels.RequestModels;

namespace AlexaGPT.Controllers;

[ApiController]
[Route("reply")]
public class ReplyController : ControllerBase
{
    private readonly IOpenAIService _openAi;

    public ReplyController()
    {
        _openAi = new OpenAIService(new OpenAiOptions
        {
            ApiKey = Environment.GetEnvironmentVariable("OPEN_AI_KEY") ??
                     throw new InvalidOperationException("OPEN_AI_KEY is not defined")
        });
    }

    [HttpPost]
    public async Task<string> Post(IFormFile audio)
    {
        var transcription = await _openAi.Audio.CreateTranscription(new AudioCreateTranscriptionRequest
        {
            FileName = $"audio.{Path.GetExtension(audio.FileName)}",
            FileStream = audio.OpenReadStream(),
            Language = "en",
            ResponseFormat = "text",
            Model = "whisper-1",
            Temperature = 0.2f
        });

        if (!transcription.Successful)
            return transcription.Error is null
                ? "Unknown error occured when connecting to OpenAI speech-to-text API"
                : $"Error {transcription.Error.Code}: {transcription.Error.Message}";

        var result = await _openAi.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
        {
            Messages = new List<ChatMessage>
            {
                ChatMessage.FromSystem("You are a versatile assistant"),
                ChatMessage.FromUser(transcription.Text)
            },
            Model = Models.ChatGpt3_5Turbo
        });

        return result.Successful
            ? result.Choices.First().Message.Content
            : result.Error is null
                ? "Unknown error occured when connecting to OpenAI chat API"
                : $"Error {result.Error.Code}: {result.Error.Message}";
    }
}
