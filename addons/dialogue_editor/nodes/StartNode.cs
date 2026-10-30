using Godot;

[Tool]
public partial class StartNode : DialogueNodeTemplate
{
    public override void _Ready()
    {
        NodeType = "start";
        NodeId = "start";
        Name = "start";
        Title = "Initial Dialogue";

        ClearAllSlots();
        AddOutput(0);
    }

    public override bool HasInput() => false;
    public override bool HasOutput() => true;
    public override int GetPrimaryOutputSlot() => 0;

    public override string ExportBody(DialogueEditor editor)
    {
        string next = editor.GetConnectionFrom(Name, 0);
        return $"type=start\nnext={next}";
    }
}
