using Godot;

[Tool]
public partial class DialogueEditorPlugin : EditorPlugin
{
	private Control _editorInstance;
	private Button _bottomPanelButton;
	private Control _baseControl;

	public override void _EnterTree()
	{
		var scene = GD.Load<PackedScene>("res://addons/dialogue_editor/DialogueEditor.tscn");
		_editorInstance = scene?.Instantiate<Control>();

		if (_editorInstance != null)
		{
			_baseControl = EditorInterface.Singleton.GetBaseControl();
			ApplyEditorTheme();
			_baseControl.ThemeChanged += ApplyEditorTheme;
			#pragma warning disable CS0618
			_bottomPanelButton = AddControlToBottomPanel(_editorInstance, "Dialogue Editor");
			#pragma warning restore CS0618
		}
	}

	private void ApplyEditorTheme()
	{
		if (GodotObject.IsInstanceValid(_editorInstance)) _editorInstance.Theme = EditorInterface.Singleton.GetEditorTheme();
	}

	public override void _ExitTree()
	{
		if (GodotObject.IsInstanceValid(_baseControl)) _baseControl.ThemeChanged -= ApplyEditorTheme;
		if (_editorInstance != null)
		{
			#pragma warning disable CS0618
			RemoveControlFromBottomPanel(_editorInstance);
			#pragma warning restore CS0618
			_editorInstance.QueueFree();
		}

		_bottomPanelButton = null;
		_editorInstance = null;
		_baseControl = null;
	}
}
