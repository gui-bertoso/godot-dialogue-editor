using Godot;
using System;
using System.Linq;
using System.Reflection;

public partial class CheckDialogues : Control
{
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private)!.Invoke(target, args)!;
	private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
	private async System.Threading.Tasks.Task Settle() { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
	private static void Write(string path, string text) { using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write); file.StoreString(text); }

	public override async void _Ready()
	{
		try
		{
            GetWindow().Mode = Window.ModeEnum.Windowed;
            GetWindow().Size = new Vector2I(1280, 720);
            GetWindow().ContentScaleSize = new Vector2I(1280, 720);
            GetWindow().ContentScaleFactor = 1;
            await Settle();
			var editor = GD.Load<PackedScene>("res://addons/dialogue_editor/DialogueEditor.tscn").Instantiate<DialogueEditor>();
			editor.Theme = new Theme { DefaultFont = new SystemFont { FontNames = new[] { "Noto Sans" } }, DefaultFontSize = 16 };
			AddChild(editor);
			editor.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			await Settle();
			var graph = editor.GetNode<GraphEdit>("%GraphEdit");
			// Reproduce a dock offset rather than testing only a full-window graph.
			editor.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
			editor.Position = new Vector2(180, 80);
			editor.Size = new Vector2(1000, 560);
			await Settle();
			var popup = editor.GetNode<PopupMenu>("PopupMenu");
			var click = new Vector2(300, 180);
			bool embedded = GetViewport().GuiEmbedSubwindows;
			foreach (bool mode in new[] { true, false })
			{
				GetViewport().GuiEmbedSubwindows = mode;
				graph.Zoom = 0.8f;
				graph.ScrollOffset = new Vector2(120, 60);
				Vector2 expected = graph.GlobalPosition + click;
				if (!mode) expected += GetWindow().Position;
				void CheckPopup()
				{
					Require(popup.Visible && ((Vector2)popup.Position - expected).Length() < 3, "Context popup must open at the click inside an offset dock, embedded or native");
					popup.Hide();
				}
				Call(editor, "OnGraphGuiInput", new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, Position = click });
				CheckPopup();
				Call(editor, "OnConnectionToEmpty", new StringName("start"), 0L, click);
				CheckPopup();
				Call(editor, "OnConnectionFromEmpty", new StringName("end"), 0L, click);
				CheckPopup();
			}
			GetViewport().GuiEmbedSubwindows = embedded;
			editor.Position = Vector2.Zero;
			editor.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			await Settle();
			var dialogue = (DialogueNode)editor.CreateDialogueNode(new Vector2(350.25f, 80.5f));
			var choices = (SwitchNode)editor.CreateSwitchNode(new Vector2(710, 80));
			var random = (RandomizeNode)editor.CreateRandomizeNode(new Vector2(1050, 80));
			await Settle();
			const string text = "hello, traveller!\nC:\\nova pasta · literal \\n · unicode 🖤";
			dialogue.SetText(text); dialogue.SetOption1Text("Explore the clearing"); dialogue.SetOption2Text("Leave");
			for (int i = 0; i < 3; i++) choices.SetOptionText(i, $"Response {i + 1}");
			void Link(string from, int port, string to) => Call(editor, "OnConnectionRequest", new StringName(from), (long)port, new StringName(to), 0L);
			Link("start", 0, dialogue.Name);
			Link(dialogue.Name, 0, choices.Name); Link(dialogue.Name, 1, "end");
			Link(choices.Name, 0, random.Name); Link(choices.Name, 1, "end"); Link(choices.Name, 2, dialogue.Name);
			Link(random.Name, 0, dialogue.Name); Link(random.Name, 1, "end"); Link(random.Name, 2, choices.Name);
			Require(graph.GetConnectionList().Count == 9, "All indexed ports should connect");
			Require(editor.HasUnsavedChanges, "Edits should mark the document dirty");
			Link(dialogue.Name, 0, random.Name);
			Require(graph.GetConnectionList().Count == 9 && editor.GetConnectionFrom(dialogue.Name, 0) == random.Name, "Replacing a response must remove its previous target");
			Link(dialogue.Name, 0, choices.Name);
			string dialogueId = dialogue.Name, choicesId = choices.Name, randomId = random.Name;
			string path = "/tmp/dialogue-editor-roundtrip.dalge";
			Call(editor, "SaveDatToPath", path);
			Require(!editor.HasUnsavedChanges, "Successful save should clear dirty state");
			var parsed = DalgeParser.Load(path);
			Require(parsed[dialogueId].Text == text, "Unicode, line breaks and literal slash-n must round trip");
			Require(parsed[choicesId].NextByPort[1] == "end" && parsed[choicesId].NextByPort[2] == dialogueId, "Choice ports 2 and 3 must serialize independently");
			Require(parsed[randomId].NextByPort[2] == choicesId, "Random third output must retain its port index");
			graph.GetNode<GraphNode>("end").PositionOffset = new Vector2(1100, 460);
			Call(editor, "SaveDatToPath", path);
			string before = editor.BuildDat();
			editor.LoadDat(path); await Settle(); await Settle();
			if (editor.BuildDat() != before) { Write("/tmp/dialogue-before.txt", before); Write("/tmp/dialogue-after.txt", editor.BuildDat()); GD.Print("CONNECTIONS=", graph.GetConnectionList().Count); }
			Require(graph.GetConnectionList().Count == 9 && editor.BuildDat() == before, "Editor round trip must preserve graph, ports, text and fractional positions");
			Write("/tmp/dialogue-editor-invalid.dalge", "[same]\ntype=start\n[same]\ntype=end");
			editor.LoadDat("/tmp/dialogue-editor-invalid.dalge");
			Require(editor.BuildDat() == before, "Malformed open must preserve the current graph");
			var restored = graph.GetNode<DialogueNode>(dialogueId);
			restored.SetText(text + " edited");
			Require(editor.HasUnsavedChanges, "Text edits must update the dirty flag");
			Call(editor, "SaveDatToPath", "/tmp/horizons-missing-dialogue-folder/test.dalge");
			Require(editor.HasUnsavedChanges, "Failed save must not discard the dirty state");
			Call(editor, "OnFileMenuPressed", 100L);
			Require(editor.BuildDat().Contains("edited"), "New graph must wait for discard confirmation");
			foreach (var dialog in editor.GetChildren().OfType<ConfirmationDialog>()) dialog.Hide();
			editor.LoadDat(path); await Settle(); await Settle();
			Require(editor.ValidateGraph().Count == 0, "Complete test graph should validate");
			GD.Print("DIALOGUE EDITOR CHECK PASSED: graph round trip, indexed ports, unicode, malformed input and save protection");
			GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
	}
}
