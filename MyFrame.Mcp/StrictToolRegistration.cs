using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MyFrame.Mcp;

public static class StrictToolRegistration
{
    public static IReadOnlyList<McpServerTool> Create(JsonSerializerOptions json)
    {
        var schema = new AIJsonSchemaCreateOptions
        {
            TransformOptions = new AIJsonSchemaTransformOptions
            {
                DisallowAdditionalProperties = true
            }
        };
        return typeof(MyFrameTools).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Select(method => McpServerTool.Create(method,
                context => context.Services!.GetRequiredService<MyFrameTools>(),
                new McpServerToolCreateOptions
                {
                    SerializerOptions = json,
                    SchemaCreateOptions = schema
                }))
            .Select(tool => (McpServerTool)new StrictMcpServerTool(tool))
            .ToArray();
    }
}

internal sealed class StrictMcpServerTool : DelegatingMcpServerTool
{
    private readonly HashSet<string> _allowedArguments;

    public StrictMcpServerTool(McpServerTool innerTool) : base(innerTool)
    {
        _allowedArguments = ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject()
            .Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
    }

    public override ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        var unknown = request.Params?.Arguments?.Keys.FirstOrDefault(key => !_allowedArguments.Contains(key));
        if (unknown is not null)
            throw new McpException($"INVALID_ARGUMENT: Unknown argument '{unknown}'. Retryable=false.");
        return base.InvokeAsync(request, cancellationToken);
    }
}
