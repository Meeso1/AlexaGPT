using ElevenLabs;
using Microsoft.AspNetCore.Mvc;
using NAudio.Wave;
using NLayer.NAudioSupport;
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
    private readonly ElevenLabsClient _elevenLabs;
    private readonly IOpenAIService _openAi;

    public ReplyController()
    {
        _openAi = new OpenAIService(new OpenAiOptions
        {
            ApiKey = Environment.GetEnvironmentVariable("OPEN_AI_KEY") ??
                     throw new InvalidOperationException("OPEN_AI_KEY is not defined")
        });
        _elevenLabs = new ElevenLabsClient(ElevenLabsAuthentication.LoadFromEnv());
    }

    [HttpPost]
    public async Task<ActionResult> Post(IFormFile audio)
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
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = transcription.Error is null
                    ? "Unknown error occured when connecting to OpenAI speech-to-text API"
                    : $"Error {transcription.Error.Code}: {transcription.Error.Message}"
            });

        var result = await _openAi.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
        {
            Messages = new List<ChatMessage>
            {
                ChatMessage.FromSystem("You are a helpful voice assistant, answering questions. All your answers"
                                       + " will be converted to speech, so avoid using symbols that are difficult to "
                                       + "convert (i.e. '```' symbols for code snippets)"),
                ChatMessage.FromUser(transcription.Text)
            },
            Model = Models.ChatGpt3_5Turbo
        });

        if (!result.Successful)
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = result.Error is null
                    ? "Unknown error occured when connecting to OpenAI chat API"
                    : $"Error {result.Error.Code}: {result.Error.Message}"
            });

        var voice = (await _elevenLabs.VoicesEndpoint.GetAllVoicesAsync()).First(voice => voice.Name == "Elli");
        var defaults = await _elevenLabs.VoicesEndpoint.GetDefaultVoiceSettingsAsync();
        var path = await _elevenLabs.TextToSpeechEndpoint.TextToSpeechAsync(result.Choices.First().Message.Content,
            voice, defaults, deleteCachedFile: true);

        var wavPath = Path.ChangeExtension(path, "wav");
        await using (var reader = new Mp3FileReaderBase(path, wf => new Mp3FrameDecompressor(wf)))
        {
            await using var stream = WaveFormatConversionStream.CreatePcmStream(reader);
            WaveFileWriter.CreateWaveFile(wavPath, stream);
        }

        return File(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read), "audio/wav");
    }
}
