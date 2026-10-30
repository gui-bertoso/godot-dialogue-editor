using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class DialogueEditor
{
	private string _savedContent = "";
	private ConfirmationDialog _discardDialog;
	private AcceptDialog _validationDialog;
	private Action _afterDiscard;
	public bool HasUnsavedChanges => graph != null && !_isBulkLoading && BuildDat() != _savedContent;

	private void BuildNativeLayout()
	{
		var toolbar = fileMenuButton.GetParent();
		var add = new MenuButton { Text = "Add Node" };
		toolbar.AddChild(add);
		toolbar.MoveChild(add, 1);
		var items = add.GetPopup();
		foreach (var entry in new[] { ("Dialogue", MenuAddDialogueNode), ("Choices", MenuAddSwitchNode), ("Random", MenuAddRandomizeNode), ("Start", MenuAddStartNode), ("End", MenuAddEndNode) }) items.AddItem(entry.Item1, entry.Item2);
		items.IdPressed += id =>
		{
			isCreatingFromConnection = false;
			popupSpawnPosition = (graph.ScrollOffset + graph.Size * 0.5f) / graph.Zoom;
			OnPopupMenuIdPressed(id);
		};
		browseLoadButton.Hide(); browseSaveButton.Hide(); loadDatButton.Hide(); refreshPreviewButton.Hide();
		filePathEdit.PlaceholderText = "Untitled — save to choose a file";
		filePathEdit.Editable = false;
		toolbar.GetNode<Control>("PathLabel").Hide();
		saveDatButton.Reparent(toolbar, false);
		saveDatButton.Text = "Save";
		var frame = new Button { Text = "Frame All", TooltipText = "Fit all dialogue nodes in view" };
		toolbar.AddChild(frame); frame.Pressed += FrameGraph;
		var validate = new Button { Text = "Validate" };
		toolbar.AddChild(validate);
		_validationDialog = new AcceptDialog { Title = "Dialogue Validation" };
		AddChild(_validationDialog);
		validate.Pressed += () =>
		{
			var issues = ValidateGraph();
			_validationDialog.DialogText = issues.Count == 0 ? "No issues found." : string.Join("\n", issues.Take(20));
			_validationDialog.PopupCentered(new Vector2I(600, 300));
		};
		var source = new Button { Text = "Source", ToggleMode = true };
		toolbar.AddChild(source);
		var panel = GetNode<Control>("Root/MainSplit/RightPanel");
		panel.Hide(); source.Toggled += visible => { panel.Visible = visible; if (visible) RefreshPreview(); };
		GetNode<Control>("Root/Toolbar/ToolbarMargin/ToolbarContent/BottomRow").Hide();
		previewText.Editable = false;
		previewText.GuttersDrawLineNumbers = true;
		graph.FocusMode = FocusModeEnum.All;
		graph.SnappingEnabled = true;
		graph.SnappingDistance = 24;
		graph.DeleteNodesRequest += _ => DeleteSelectedNodes();
		graph.ChildEnteredTree += node =>
		{
			if (node is DialogueNodeTemplate dialogue) Callable.From(() => ObserveNode(dialogue)).CallDeferred();
		};
		_discardDialog = new ConfirmationDialog { Title = "Unsaved Dialogue", DialogText = "Discard unsaved changes to this dialogue?", OkButtonText = "Discard Changes" };
		AddChild(_discardDialog);
		_discardDialog.Confirmed += () => { var action = _afterDiscard; _afterDiscard = null; action?.Invoke(); };
		_discardDialog.Canceled += () => _afterDiscard = null;
	}

	private void ObserveNode(DialogueNodeTemplate node)
	{
		if (!IsInstanceValid(node) || !node.IsInsideTree() || node.HasMeta("dialogue_observed")) return;
		node.SetMeta("dialogue_observed", true);
		node.PositionOffsetChanged += RefreshPreview;
		void Observe(Node child)
		{
			if (child is LineEdit line) line.TextChanged += _ => RefreshPreview();
			if (child is TextEdit text) text.TextChanged += RefreshPreview;
			foreach (var descendant in child.GetChildren()) Observe(descendant);
		}
		Observe(node);
	}

	private void RequestReplace(Action action)
	{
		if (_isBulkLoading) return;
		if (!HasUnsavedChanges) { action(); return; }
		_afterDiscard = action;
		_discardDialog.PopupCentered(new Vector2I(460, 150));
		_discardDialog.GetCancelButton().GrabFocus();
	}

	private void FrameGraph()
	{
		var nodes = graph.GetChildren().OfType<DialogueNodeTemplate>().ToList();
		if (nodes.Count == 0) return;
		var bounds = new Rect2(nodes[0].PositionOffset, nodes[0].Size);
		foreach (var node in nodes.Skip(1)) bounds = bounds.Merge(new Rect2(node.PositionOffset, node.Size));
		graph.Zoom = Mathf.Clamp(Mathf.Min(graph.Size.X / (bounds.Size.X + 160), graph.Size.Y / (bounds.Size.Y + 100)), graph.ZoomMin, 1.0f);
		graph.ScrollOffset = bounds.GetCenter() * graph.Zoom - graph.Size * 0.5f;
	}

	public List<string> ValidateGraph()
	{
		var issues = new List<string>();
		var connections = CaptureConnections();
		var nodes = graph.GetChildren().OfType<DialogueNodeTemplate>().ToList();
		if (nodes.Count(node => node.NodeType == "start") != 1) issues.Add("The graph needs exactly one Start node.");
		foreach (var node in nodes)
		{
			if (node.NodeType == "end") continue;
			for (int port = 0; port < node.GetOutputPortCount(); port++)
				if (!connections.TryGetValue(node.Name, out var ports) || !ports.ContainsKey(port)) issues.Add($"{node.Title}: output {port + 1} is not connected.");
		}
		return issues;
	}

	private void UpdateDocumentStatus(string dat)
	{
		int nodes = graph.GetChildren().OfType<DialogueNodeTemplate>().Count();
		int issues = ValidateGraph().Count;
		UpdateStatus($"{GetFileNameFromPath(_currentFilePath)}{(dat != _savedContent ? " *" : "")}   ·   {nodes} nodes   ·   {graph.GetConnectionList().Count} connections   ·   {issues} issues");
	}
}
