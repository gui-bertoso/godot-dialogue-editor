using Godot;
using System.Collections.Generic;
using System.Text;

[Tool]
public partial class DialogueNodeTemplate : GraphNode
{
    public string NodeType = "base";
    public string NodeId = "";

    public virtual void SetupNode(string type, string prefix)
    {
        if (!string.IsNullOrEmpty(NodeId))
            return;

        NodeType = type;
        NodeId = $"{prefix}_{GD.Randi()}";
        Name = NodeId;
    }

    public virtual int GetPrimaryInputSlot() => 0;
    public virtual int GetPrimaryOutputSlot() => 0;
    public virtual bool HasInput() => true;
    public virtual bool HasOutput() => true;

    public virtual string ExportBody(DialogueEditor editor)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"type={NodeType}");

        AppendCustomData(sb, editor);
        AppendConnections(sb, editor);

        return sb.ToString().TrimEnd();
    }

	public virtual int VisualOutputSlotToPort(int visualSlot)
	{
		return visualSlot;
	}

	public virtual int PortToVisualOutputSlot(int port)
	{
		return port;
	}

    protected virtual void AppendCustomData(StringBuilder sb, DialogueEditor editor)
    {
    }

    protected virtual void AppendConnections(StringBuilder sb, DialogueEditor editor)
    {
        Dictionary<int, string> connections = editor.GetConnectionsFrom(Name);

        foreach (var pair in connections)
            sb.AppendLine($"next_{pair.Key}={pair.Value}");
    }

    protected static string EscapeText(string text)
    {
        return (text ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("\n", "\\n")
            .Replace("\r", "");
    }

    protected static string UnescapeText(string text)
    {
        return (text ?? string.Empty)
            .Replace("\\n", "\n")
            .Replace("\\\\", "\\");
    }

    public void Setup(string id, string type)
    {
        NodeId = id;
        NodeType = type;
        Name = id;
        Title = id;
    }

	protected void ClearAllSlots(int maxSlots = 10)
	{
		for (int i = 0; i < maxSlots; i++)
		{
			SetSlotEnabledLeft(i, false);
			SetSlotEnabledRight(i, false);
			SetSlotTypeLeft(i, 0);
			SetSlotTypeRight(i, 0);
			SetSlotColorLeft(i, Colors.White);
			SetSlotColorRight(i, Colors.White);
		}
	}

	protected void AddInput(int slot)
	{
		SetSlotEnabledLeft(slot, true);
		SetSlotTypeLeft(slot, 0);
		SetSlotColorLeft(slot, Colors.White);
	}

	protected void AddOutput(int slot)
	{
		SetSlotEnabledRight(slot, true);
		SetSlotTypeRight(slot, 0);
		SetSlotColorRight(slot, Colors.White);
	}
}
