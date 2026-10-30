using Godot;
using System.Text;

[Tool]
public partial class RandomizeNode : DialogueNodeTemplate
{
    public override void _Ready()
    {
        SetupNode("randomize", "randomize");
        Title = "Randomize Node";

        ClearAllSlots();
        AddInput(0);
        AddOutput(2);
        AddOutput(3);
        AddOutput(4);
    }

    public override bool HasInput() => true;
    public override bool HasOutput() => true;
    public override int GetPrimaryInputSlot() => 0;
    public override int GetPrimaryOutputSlot() => 0;

    public override string ExportBody(DialogueEditor editor)
    {
        var sb = new StringBuilder();

        sb.AppendLine("type=randomize");

		for (int port = 0; port < 3; port++)
		{
			string next = editor.GetConnectionFrom(Name, port);
			sb.AppendLine($"option_{port}_next={next}");
		}

        return sb.ToString().TrimEnd();
    }
}
