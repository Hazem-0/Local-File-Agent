using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LocalFileAgent.Domain.Models;
using LocalFileAgent.Infrastructure.Ollama;
using Xunit;

namespace LocalFileAgent.Infrastructure.Tests;

public class OllamaClientTests
{
    [Theory]
    [InlineData("http://api.openai.com/v1")]
    [InlineData("http://192.168.1.100:11434")]
    [InlineData("https://models.internal.corp:11434")]
    [InlineData("http://8.8.8.8:11434")]
    public void Constructor_RejectsNonLoopbackEndpoints(string invalidEndpoint)
    {
        var act = () => new OllamaClient(new HttpClient(), invalidEndpoint);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*loopback*");
    }

    [Theory]
    [InlineData("http://127.0.0.1:11434")]
    [InlineData("http://localhost:11434")]
    [InlineData("http://[::1]:11434")]
    public void Constructor_AcceptsLoopbackEndpoints(string validEndpoint)
    {
        var act = () => new OllamaClient(new HttpClient(), validEndpoint);

        act.Should().NotThrow();
    }

    [Fact]
    public async Task GetVersionAsync_ParsesVersionResponseSuccessfully()
    {
        var mockHandler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"version\": \"0.35.0\"}")
        });

        var client = new OllamaClient(new HttpClient(mockHandler), "http://127.0.0.1:11434");
        var version = await client.GetVersionAsync(CancellationToken.None);

        version.Should().Be("0.35.0");
    }

    [Fact]
    public async Task ListModelsAsync_ParsesTagsResponse()
    {
        var json = @"
        {
          ""models"": [
            {
              ""name"": ""bge-m3:latest"",
              ""modified_at"": ""2026-10-02T08:00:00Z"",
              ""size"": 1200000000
            }
          ]
        }";

        var mockHandler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        });

        var client = new OllamaClient(new HttpClient(mockHandler), "http://127.0.0.1:11434");
        var models = await client.ListModelsAsync(CancellationToken.None);

        models.Should().HaveCount(1);
        models[0].Name.Should().Be("bge-m3:latest");
        models[0].SizeBytes.Should().Be(1200000000);
    }

    [Fact]
    public async Task ChatAsync_SendsMessagesAndParsesResponse()
    {
        var json = @"
        {
          ""model"": ""gemma4:e2b"",
          ""message"": {
            ""role"": ""assistant"",
            ""content"": ""{\""intent\"": \""search\""}""
          },
          ""prompt_eval_count"": 45,
          ""eval_count"": 20,
          ""total_duration"": 150000000
        }";

        var mockHandler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        });

        var client = new OllamaClient(new HttpClient(mockHandler), "http://127.0.0.1:11434");
        var response = await client.ChatAsync(new ChatRequest(
            Model: "gemma4:e2b",
            Messages: new[] { new ChatMessage("user", "بحث عن ملف") }
        ), CancellationToken.None);

        response.Model.Should().Be("gemma4:e2b");
        response.Content.Should().Contain("search");
        response.PromptTokens.Should().Be(45);
        response.CompletionTokens.Should().Be(20);
    }

    [Fact]
    public async Task EmbedAsync_ParsesEmbeddingVectors()
    {
        var json = @"
        {
          ""model"": ""bge-m3"",
          ""embeddings"": [
            [0.12, -0.34, 0.56, 0.78]
          ],
          ""prompt_eval_count"": 12,
          ""total_duration"": 80000000
        }";

        var mockHandler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        });

        var client = new OllamaClient(new HttpClient(mockHandler), "http://127.0.0.1:11434");
        var response = await client.EmbedAsync(new EmbeddingRequest(
            Model: "bge-m3",
            Input: new[] { "تقرير مالي" }
        ), CancellationToken.None);

        response.Model.Should().Be("bge-m3");
        response.Embeddings.Should().HaveCount(1);
        response.Embeddings[0].Length.Should().Be(4);
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public MockHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_response);
        }
    }
}
