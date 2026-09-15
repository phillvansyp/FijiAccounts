using System.Reflection;
using FijiAccounts.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;

namespace FijiAccounts.Web.Tests;

public sealed class InvoiceEditFormTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FieldsAreDisabledOnlyWhileSaving(bool saving)
    {
        var component = new EditPostedInvoice();
        var type = typeof(EditPostedInvoice);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        type.GetField("ready", flags)!.SetValue(component, true);
        type.GetField("saving", flags)!.SetValue(component, saving);
        var model = type.GetField("model", flags)!.GetValue(component)!;
        using var page = new RenderTreeBuilder();
        type.GetMethod("BuildRenderTree", flags)!.Invoke(component, [page]);
#pragma warning disable BL0006
        var frames = page.GetFrames();
        var form = frames.Array.Take(frames.Count).Single(x => x.FrameType == RenderTreeFrameType.Attribute &&
            x.AttributeName == "ChildContent" && x.AttributeValue is RenderFragment<EditContext>);
        using var content = new RenderTreeBuilder();
        ((RenderFragment<EditContext>)form.AttributeValue)(new EditContext(model))(content);
        var fields = content.GetFrames();
        var fieldsetIndex = Array.FindIndex(fields.Array, 0, fields.Count,
            x => x.FrameType == RenderTreeFrameType.Element && x.ElementName == "fieldset");
        Assert.True(fieldsetIndex >= 0);
        var attributes = fields.Array.Skip(fieldsetIndex + 1).TakeWhile(x => x.FrameType == RenderTreeFrameType.Attribute);
        var disabled = attributes.Where(x => x.AttributeName == "disabled").ToArray();
        if (saving) Assert.Equal(true, Assert.Single(disabled).AttributeValue);
        else Assert.Empty(disabled);
#pragma warning restore BL0006
    }
}
