using System.ComponentModel;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace MyFrame.Mcp;

[McpServerPromptType]
public sealed class MyFramePrompts
{
    [McpServerPrompt(Name = "review_inventory", Title = "Review My Frame inventory")]
    [Description("Guides a read-only, source-aware inventory review using consistent snapshot pagination.")]
    public static ChatMessage ReviewInventory(
        [Description("Optional focus such as farm, platinum, ducats, relics, or collection.")] string focus = "overall") =>
        new(ChatRole.User, $"""
            Review my My Frame inventory with focus on {focus}. Call get_overview first and reuse its
            snapshotId in all related calls. Follow every nextCursor needed for the selected analysis.
            Treat missing/stale prices and unconfirmed order availability as uncertainty. Do not add
            list_surplus totals to list_sales totals because the lists overlap. Explain decisions using
            reasonCode and evidence, and identify partial coverage before giving economic advice.
            """);
}
