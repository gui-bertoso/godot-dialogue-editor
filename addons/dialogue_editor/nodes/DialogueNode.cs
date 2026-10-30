using Godot;
using System.Text;
using System.Collections.Generic;

[Tool]
public partial class DialogueNode : DialogueNodeTemplate
{
    private TextEdit textEdit;
    private LineEdit option1;
    private LineEdit option2;

    public override void _Ready()
    {
        SetupNode("dialogue", "dialogue");
        Title = "Dialogue Node";

        textEdit = GetNodeOrNull<TextEdit>("%TextEdit");
        option1 = GetNodeOrNull<LineEdit>("%Option1");
        option2 = GetNodeOrNull<LineEdit>("%Option2");

        if (textEdit == null)
            GD.PrintErr($"DialogueNode {Name}: %TextEdit not found.");

        if (option1 == null)
            GD.PrintErr($"DialogueNode {Name}: %Option1 not found.");

        if (option2 == null)
            GD.PrintErr($"DialogueNode {Name}: %Option2 not found.");

        ClearAllSlots();
        AddInput(0);
        AddOutput(1);
        AddOutput(2);
    }

    public override bool HasInput() => true;
    public override bool HasOutput() => true;
    public override int GetPrimaryInputSlot() => 0;
    public override int GetPrimaryOutputSlot() => 0;

    public override string ExportBody(DialogueEditor editor)
    {
        var sb = new StringBuilder();

        string mainText = EscapeText(textEdit?.Text ?? "");
        string option1Text = EscapeText(option1?.Text ?? "");
        string option2Text = EscapeText(option2?.Text ?? "");

        Dictionary<int, string> connections = editor.GetConnectionsFrom(Name);

        string next1 = "";
        string next2 = "";

        int portOption1 = 0;
        int portOption2 = 1;

        if (connections.TryGetValue(portOption1, out var c1))
            next1 = c1;

        if (connections.TryGetValue(portOption2, out var c2))
            next2 = c2;

        sb.AppendLine("type=dialogue");
        sb.AppendLine($"text={mainText}");
        sb.AppendLine($"option_0_text={option1Text}");
        sb.AppendLine($"option_0_next={next1}");
        sb.AppendLine($"option_1_text={option2Text}");
        sb.AppendLine($"option_1_next={next2}");

        return sb.ToString().TrimEnd();
    }

    public void SetText(string text)
    {
        if (textEdit != null)
            textEdit.Text = text;
    }

    public void SetOption1Text(string text)
    {
        if (option1 != null)
            option1.Text = text;
    }

    public void SetOption2Text(string text)
    {
        if (option2 != null)
            option2.Text = text;
    }
}
