﻿

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntimeGenAI;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Services;

namespace feiyun0112.SemanticKernel.Connectors.OnnxRuntimeGenAI;

/// <summary>
/// Represents a chat completion service using OnnxRuntimeGenAI.
/// </summary>
public sealed class OnnxRuntimeGenAIChatCompletionService : IChatCompletionService
{
    private readonly Model _model;
    private readonly Tokenizer _tokenizer;

    private Dictionary<string, object?> AttributesInternal { get; } = new();

    /// <summary>
    /// Initializes a new instance of the OnnxRuntimeGenAIChatCompletionService class.
    /// </summary>
    /// <param name="modelPath">The generative AI ONNX model path for the chat completion service.</param>
    /// <param name="loggerFactory">Optional logger factory to be used for logging.</param>
    public OnnxRuntimeGenAIChatCompletionService(
        string modelPath,
        ILoggerFactory? loggerFactory = null)
    {
        _model = new Model(modelPath);
        _tokenizer = new Tokenizer(_model);

        this.AttributesInternal.Add(AIServiceExtensions.ModelIdKey, _tokenizer);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> Attributes => this.AttributesInternal;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default)
    {
        var result = new StringBuilder();

        await foreach (var content in RunInferenceAsync(chatHistory, executionSettings, cancellationToken))
        {
            result.Append(content);
        }

        return new List<ChatMessageContent>
        {
            new(
                role: AuthorRole.Assistant,
                content: result.ToString())
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null, CancellationToken cancellationToken = default)
    {
        await foreach (var content in RunInferenceAsync(chatHistory, executionSettings, cancellationToken))
        {
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, content);
        }
    }

    private async IAsyncEnumerable<string> RunInferenceAsync(ChatHistory chatHistory, PromptExecutionSettings? executionSettings, CancellationToken cancellationToken)
    {
        OnnxRuntimeGenAIPromptExecutionSettings onnxRuntimeGenAIPromptExecutionSettings = OnnxRuntimeGenAIPromptExecutionSettings.FromExecutionSettings(executionSettings);

        var prompt = GetPrompt(chatHistory, onnxRuntimeGenAIPromptExecutionSettings);
        var tokens = _tokenizer.Encode(prompt);

        var generatorParams = new GeneratorParams(_model);
        ApplyPromptExecutionSettings(generatorParams, onnxRuntimeGenAIPromptExecutionSettings);
        generatorParams.SetInputSequences(tokens);

        var generator = new Generator(_model, generatorParams);

        while (!generator.IsDone())
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return await Task.Run(() =>
            {
                generator.ComputeLogits();
                generator.GenerateNextToken();

                var outputTokens = generator.GetSequence(0);
                var newToken = outputTokens.Slice(outputTokens.Length - 1, 1);
                var output = _tokenizer.Decode(newToken);
                return output;
            }, cancellationToken);
        }
    }

    private string GetPrompt(ChatHistory chatHistory, OnnxRuntimeGenAIPromptExecutionSettings onnxRuntimeGenAIPromptExecutionSettings)
    {
        var promptBuilder = new StringBuilder();
        foreach (var message in chatHistory)
        {
            promptBuilder.Append($"<|{message.Role}|>\n{message.Content}");
        }
        promptBuilder.Append($"<|end|>\n<|assistant|>");

        return promptBuilder.ToString();
    }

    private void ApplyPromptExecutionSettings(GeneratorParams generatorParams, OnnxRuntimeGenAIPromptExecutionSettings onnxRuntimeGenAIPromptExecutionSettings)
    {
        generatorParams.SetSearchOption("top_p", onnxRuntimeGenAIPromptExecutionSettings.TopP);
        generatorParams.SetSearchOption("top_k", onnxRuntimeGenAIPromptExecutionSettings.TopK);
        generatorParams.SetSearchOption("temperature", onnxRuntimeGenAIPromptExecutionSettings.Temperature);
        generatorParams.SetSearchOption("repetition_penalty", onnxRuntimeGenAIPromptExecutionSettings.RepetitionPenalty);
        generatorParams.SetSearchOption("past_present_share_buffer", onnxRuntimeGenAIPromptExecutionSettings.PastPresentShareBuffer);
        generatorParams.SetSearchOption("num_return_sequences", onnxRuntimeGenAIPromptExecutionSettings.NumReturnSequences);
        generatorParams.SetSearchOption("no_repeat_ngram_size", onnxRuntimeGenAIPromptExecutionSettings.NoRepeatNgramSize);
        generatorParams.SetSearchOption("min_length", onnxRuntimeGenAIPromptExecutionSettings.MinLength);
        generatorParams.SetSearchOption("max_length", onnxRuntimeGenAIPromptExecutionSettings.MaxLength);
        generatorParams.SetSearchOption("length_penalty", onnxRuntimeGenAIPromptExecutionSettings.LengthPenalty);
        generatorParams.SetSearchOption("early_stopping", onnxRuntimeGenAIPromptExecutionSettings.EarlyStopping);
        generatorParams.SetSearchOption("do_sample", onnxRuntimeGenAIPromptExecutionSettings.DoSample);
        generatorParams.SetSearchOption("diversity_penalty", onnxRuntimeGenAIPromptExecutionSettings.DiversityPenalty);
    }
}
