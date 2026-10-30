using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;

[Tool]
public partial class DialogueEditor : Control
{
	private const int MenuAddDialogueNode = 0;
	private const int MenuAddSwitchNode = 1;
	private const int MenuAddStartNode = 2;
	private const int MenuDeleteSelected = 3;
	private const int MenuDisconnectSelected = 4;
	private const int MenuAddEndNode = 5;
	private const int MenuAddRandomizeNode = 6;
	private const int FileMenuNew = 100;
	private const int FileMenuOpen = 101;
	private const int FileMenuSave = 102;
	private const int FileMenuSaveAs = 103;
	private const int LoadBatchSize = 8;
	private const string DefaultDialoguePath = "res://addons/dialogue_editor/output/dialogue_preview.dalge";

	private bool isCreatingFromConnection = false;
	private StringName pendingFromNode;
	private int pendingFromSlot;
	private bool pendingFromOutput = true;

	private GraphEdit graph;
	private PopupMenu popupMenu;

	private PackedScene dialogueNodeScene;
	private PackedScene startNodeScene;
	private PackedScene switchNodeScene;

	private Vector2 popupSpawnPosition;

	private CodeEdit previewText;
	private Button refreshPreviewButton;
	private Button saveDatButton;
	private Button loadDatButton;
	private Button browseLoadButton;
	private Button browseSaveButton;
	private LineEdit filePathEdit;
	private Label statusLabel;
	private MenuButton fileMenuButton;
	private PopupMenu fileMenuPopup;
	private FileDialog openFileDialog;
	private FileDialog saveFileDialog;

	private PackedScene endNodeScene;
	private PackedScene randomizeNodeScene;
	private bool _isRefreshingPreview;
	private Timer _refreshTimer;
	internal int PreviewFlushCount { get; private set; }
	private Dictionary<StringName, Dictionary<int, string>> _exportConnections;
	private bool _isBulkLoading;
	private string _currentFilePath = DefaultDialoguePath;

	public override void _Ready()
	{
		graph = GetNodeOrNull<GraphEdit>("%GraphEdit")
			?? GetNodeOrNull<GraphEdit>("Root/MainSplit/LeftPanel/LeftMargin/GraphEdit")
			?? GetNodeOrNull<GraphEdit>("LeftPanel/GraphEdit");
		popupMenu = GetNode<PopupMenu>("PopupMenu");
		previewText = GetNode<CodeEdit>("%PreviewText");
		refreshPreviewButton = GetNode<Button>("%RefreshPreviewButton");
		saveDatButton = GetNode<Button>("%SaveDatButton");
		loadDatButton = GetNode<Button>("%LoadDatButton");
		browseLoadButton = GetNode<Button>("%BrowseLoadButton");
		browseSaveButton = GetNode<Button>("%BrowseSaveButton");
		filePathEdit = GetNode<LineEdit>("%FilePathEdit");
		statusLabel = GetNode<Label>("%StatusLabel");
		fileMenuButton = GetNode<MenuButton>("%FileMenuButton");
		openFileDialog = GetNode<FileDialog>("%OpenFileDialog");
		saveFileDialog = GetNode<FileDialog>("%SaveFileDialog");

		if (graph == null)
		{
			GD.PrintErr("DialogueEditor: GraphEdit not found.");
			return;
		}

		refreshPreviewButton.Pressed += RefreshPreview;
		saveDatButton.Pressed += SaveDat;
		loadDatButton.Pressed += LoadCurrentPath;
		browseLoadButton.Pressed += OpenLoadDialog;
		browseSaveButton.Pressed += OpenSaveDialog;
		filePathEdit.TextSubmitted += OnFilePathSubmitted;

		dialogueNodeScene = GD.Load<PackedScene>("res://addons/dialogue_editor/nodes/DialogueNode.tscn");
		startNodeScene = GD.Load<PackedScene>("res://addons/dialogue_editor/nodes/StartNode.tscn");
		switchNodeScene = GD.Load<PackedScene>("res://addons/dialogue_editor/nodes/SwitchNode.tscn");
		endNodeScene = GD.Load<PackedScene>("res://addons/dialogue_editor/nodes/EndNode.tscn");
		randomizeNodeScene = GD.Load<PackedScene>("res://addons/dialogue_editor/nodes/RandomizeNode.tscn");

		graph.ConnectionRequest += OnConnectionRequest;
		graph.DisconnectionRequest += OnDisconnectionRequest;
		graph.ConnectionToEmpty += OnConnectionToEmpty;
		graph.ConnectionFromEmpty += OnConnectionFromEmpty;
		graph.GuiInput += OnGraphGuiInput;

		popupMenu.IdPressed += OnPopupMenuIdPressed;
		fileMenuPopup = fileMenuButton.GetPopup();
		fileMenuPopup.IdPressed += OnFileMenuPressed;
		openFileDialog.FileSelected += OnOpenFileSelected;
		saveFileDialog.FileSelected += OnSaveFileSelected;

		_refreshTimer = new Timer { WaitTime = 0.15, OneShot = true };
		AddChild(_refreshTimer);
		_refreshTimer.Timeout += FlushPreview;
		SetupPopupMenu();
		SetupFileMenu();
		SetCurrentFilePath("", false);
		BuildNativeLayout();

		if (!HasStartNode())
			CreateStartNode();

		if (!HasEndNode())
			CreateEndNode();

		_savedContent = BuildDat();
		RefreshPreview();
	}

	public string GetFirstConnectionFrom(StringName nodeName, int slot) => GetConnectionFrom(nodeName, slot);

	private Dictionary<StringName, Dictionary<int, string>> CaptureConnections()
	{
		var result = new Dictionary<StringName, Dictionary<int, string>>();
		foreach (var connection in graph.GetConnectionList())
		{
			var name = connection["from_node"].AsStringName();
			if (!result.TryGetValue(name, out var ports)) result[name] = ports = new();
			ports[connection["from_port"].AsInt32()] = connection["to_node"].AsString();
		}
		return result;
	}

	private bool HasEndNode()
	{
		foreach (Node child in graph.GetChildren())
		{
			if (child is DialogueNodeTemplate baseNode && baseNode.NodeType == "end")
				return true;
		}

		return false;
	}

	public void CreateEndNode()
	{
		CreateEndNodeAt(new Vector2(560, 100));
	}

	public GraphNode CreateEndNodeAt(Vector2 pos)
	{
		if (HasEndNode())
			return null;

		var node = endNodeScene.Instantiate<GraphNode>();
		graph.AddChild(node);
		node.PositionOffset = pos;
		return node;
	}

	public string GetConnectionFrom(StringName nodeName, int slot)
	{
		var connections = _exportConnections ?? CaptureConnections();
		return connections.TryGetValue(nodeName, out var ports) ? ports.GetValueOrDefault(slot, "") : "";
	}

	public bool HasConnectionFrom(StringName nodeName, int slot)
	{
		return !string.IsNullOrEmpty(GetConnectionFrom(nodeName, slot));
	}

	public string BuildDat()
	{
		// Export all nodes against one snapshot instead of copying and scanning
		// the native GraphEdit connection array once for every output.
		var previous = _exportConnections;
		_exportConnections = CaptureConnections();
		try
		{
			var sb = new System.Text.StringBuilder();
			foreach (Node child in graph.GetChildren())
			{
				if (child is not DialogueNodeTemplate node) continue;
				Vector2 pos = node.PositionOffset;
				sb.AppendLine($"[{node.Name}]");
				sb.AppendLine($"position={pos.X.ToString("R", CultureInfo.InvariantCulture)},{pos.Y.ToString("R", CultureInfo.InvariantCulture)}");
				sb.AppendLine(node.ExportBody(this).TrimEnd());
				sb.AppendLine();
			}
			return sb.ToString().TrimEnd();
		}
		finally { _exportConnections = previous; }
	}

	private void RefreshPreview()
	{
		if (_isBulkLoading || _isRefreshingPreview) return;
		// Editing/dragging emits many signals. Do the expensive work once after
		// a short idle interval, never in the drag callback itself.
		_refreshTimer?.Start();
	}

	private void FlushPreview()
	{
		if (_isBulkLoading || _isRefreshingPreview) return;
		_isRefreshingPreview = true;
		try
		{
			PreviewFlushCount++;
			string dat = BuildDat();
			if (previewText.IsVisibleInTree() && previewText.Text != dat) previewText.Text = dat;
			UpdateDocumentStatus(dat);
		}
		finally { _isRefreshingPreview = false; }
	}

	private void SaveDat()
	{
		if (_isBulkLoading) return;
		string path = GetValidatedPathInput();
		if (string.IsNullOrWhiteSpace(path)) OpenSaveDialog();
		else SaveDatToPath(path);
	}

	private void SaveDatToPath(string path)
	{
		if (_isBulkLoading || string.IsNullOrWhiteSpace(path)) return;
		if (string.IsNullOrEmpty(path.GetExtension())) path += ".dalge";
		string temporary = path + ".tmp";
		try
		{
			string dat = BuildDat();
			using (var file = FileAccess.Open(temporary, FileAccess.ModeFlags.Write))
			{
				if (file == null) throw new System.IO.IOException(FileAccess.GetOpenError().ToString());
				file.StoreString(dat);
				file.Flush();
				if (file.GetError() != Error.Ok) throw new System.IO.IOException(file.GetError().ToString());
			}
			var result = DirAccess.RenameAbsolute(ProjectSettings.GlobalizePath(temporary), ProjectSettings.GlobalizePath(path));
			if (result != Error.Ok) throw new System.IO.IOException(result.ToString());
			SetCurrentFilePath(path, false);
			_savedContent = dat;
			if (previewText.IsVisibleInTree()) previewText.Text = dat;
			UpdateStatus($"Saved: {path}");
		}
		catch (Exception error)
		{
			DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(temporary));
			UpdateStatus($"Could not save: {error.Message}");
		}
	}

	public async void LoadDat(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
			return;

		if (_isBulkLoading) return;
		Dictionary<string, DalgeData> data;
		try
		{
			data = DalgeParser.Load(path);
			if (data.Count == 0) throw new FormatException("The file contains no dialogue nodes.");
			foreach (var node in data.Values)
			{
				if (node.Id.ValidateNodeName() != node.Id) throw new FormatException($"Invalid node ID: {node.Id}");
				if (node.Type is not ("start" or "dialogue" or "switch" or "randomize" or "end"))
					throw new FormatException($"Unknown node type: {node.Type}");
			}
		}
		catch (Exception error) { UpdateStatus($"Could not open: {error.Message}"); return; }

		_isBulkLoading = true;
		graph.Visible = false;
		graph.MouseFilter = MouseFilterEnum.Ignore;
		UpdateStatus($"Loading {GetFileNameFromPath(path)}...");

		try
		{
			ClearGraph();

			var createdNodes = new Dictionary<string, DialogueNodeTemplate>();
			int processedNodes = 0;

			foreach (var pair in data)
			{
				DalgeData nodeData = pair.Value;

				if (string.IsNullOrWhiteSpace(nodeData.Type))
					continue;

				DialogueNodeTemplate node = CreateNodeFromType(nodeData.Type);
				if (node == null)
					continue;

				graph.AddChild(node);

				node.Name = nodeData.Id;
				node.NodeId = nodeData.Id;
				node.NodeType = nodeData.Type;
				node.PositionOffset = nodeData.Position;

				ApplyNodeData(node, nodeData);
				createdNodes[nodeData.Id] = node;

				processedNodes++;
				if (processedNodes % LoadBatchSize == 0)
				{
					UpdateStatus($"Loading nodes... {processedNodes}/{data.Count}");
					await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				}
			}

			// GraphNode builds its port cache during the container layout pass.
			graph.Visible = true;
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

			int processedConnections = 0;
			foreach (var pair in data)
			{
				DalgeData nodeData = pair.Value;

				if (!createdNodes.TryGetValue(nodeData.Id, out var fromNode))
					continue;

				foreach (var connectionPair in nodeData.NextByPort)
				{
					int fromPort = connectionPair.Key;
					string targetId = connectionPair.Value;

					if (string.IsNullOrWhiteSpace(targetId))
						continue;

					if (!createdNodes.TryGetValue(targetId, out var toNode))
						continue;

					int visualFromPort = fromNode.PortToVisualOutputSlot(fromPort);
					int toPort = toNode.GetPrimaryInputSlot();

					if (visualFromPort < 0 || visualFromPort >= fromNode.GetOutputPortCount() ||
						toPort < 0 || toPort >= toNode.GetInputPortCount() || !CanConnectNodes(fromNode, toNode)) continue;
					if (!graph.IsNodeConnected(fromNode.Name, visualFromPort, toNode.Name, toPort))
						graph.ConnectNode(fromNode.Name, visualFromPort, toNode.Name, toPort);

					processedConnections++;
					if (processedConnections % LoadBatchSize == 0)
					{
						UpdateStatus($"Connecting nodes... {processedConnections}");
						await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
					}
				}
			}

			SetCurrentFilePath(path);
			_isBulkLoading = false;
			_savedContent = BuildDat();
			RefreshPreview();
			FrameGraph();
		}
		finally
		{
			graph.MouseFilter = MouseFilterEnum.Pass;
			graph.Visible = true;
			_isBulkLoading = false;
		}
	}

	private void ClearGraph()
	{
		var connections = new List<Godot.Collections.Dictionary>();

		foreach (Godot.Collections.Dictionary connection in graph.GetConnectionList())
			connections.Add(connection);

		foreach (var connection in connections)
		{
			StringName fromNode = (StringName)connection["from_node"];
			int fromPort = (int)(long)connection["from_port"];
			StringName toNode = (StringName)connection["to_node"];
			int toPort = (int)(long)connection["to_port"];

			graph.DisconnectNode(fromNode, fromPort, toNode, toPort);
		}

		var nodesToRemove = new List<DialogueNodeTemplate>();

		foreach (Node child in graph.GetChildren())
		{
			if (child is DialogueNodeTemplate dialogueNode)
				nodesToRemove.Add(dialogueNode);
		}

		foreach (var node in nodesToRemove)
		{
			if (IsInstanceValid(node) && node.GetParent() == graph)
				graph.RemoveChild(node);

			node.QueueFree();
		}
	}

	private DialogueNodeTemplate CreateNodeFromType(string type)
	{
		switch (type)
		{
			case "start":
				return startNodeScene.Instantiate<DialogueNodeTemplate>();

			case "dialogue":
				return dialogueNodeScene.Instantiate<DialogueNodeTemplate>();

			case "switch":
				return switchNodeScene.Instantiate<DialogueNodeTemplate>();

			case "randomize":
				return randomizeNodeScene.Instantiate<DialogueNodeTemplate>();

			case "end":
				return endNodeScene.Instantiate<DialogueNodeTemplate>();

			default:
				GD.PrintErr($"unknown node type: {type}");
				return null;
		}
	}

	private bool HasSelectedNode()
	{
		foreach (Node child in graph.GetChildren())
		{
			if (child is GraphNode graphNode && graphNode.Selected)
				return true;
		}

		return false;
	}

	private GraphNode GetFirstSelectedNode()
	{
		foreach (Node child in graph.GetChildren())
		{
			if (child is GraphNode graphNode && graphNode.Selected)
				return graphNode;
		}

		return null;
	}

	private bool HasStartNode()
	{
		foreach (Node child in graph.GetChildren())
		{
			if (child is DialogueNodeTemplate baseNode && baseNode.NodeType == "start")
				return true;
		}

		return false;
	}

	public Dictionary<int, string> GetConnectionsFrom(StringName nodeName)
	{
		var connections = _exportConnections ?? CaptureConnections();
		return connections.TryGetValue(nodeName, out var ports) ? ports : new();
	}

	private void SetupPopupMenu()
	{
		popupMenu.Clear();

		bool hasSelectedNode = HasSelectedNode();

		if (hasSelectedNode)
		{
			popupMenu.AddItem("Delete Selected", MenuDeleteSelected);
			popupMenu.AddItem("Disconnect Selected", MenuDisconnectSelected);
			popupMenu.AddSeparator();
		}

		popupMenu.AddItem("Add Dialogue", MenuAddDialogueNode);
		popupMenu.AddItem("Add Choices", MenuAddSwitchNode);
		popupMenu.AddItem("Add Random", MenuAddRandomizeNode);

		if (!HasStartNode())
		{
			popupMenu.AddSeparator();
			popupMenu.AddItem("Add Start", MenuAddStartNode);
		}
		if (!HasEndNode())
		{
			popupMenu.AddSeparator();
			popupMenu.AddItem("Add End", MenuAddEndNode);
		}

		RefreshPreview();
	}

	private void SetupFileMenu()
	{
		fileMenuPopup.Clear();
		fileMenuPopup.AddItem("New Graph", FileMenuNew);
		fileMenuPopup.AddSeparator();
		fileMenuPopup.AddItem("Open .dalge...", FileMenuOpen);
		fileMenuPopup.AddItem("Save", FileMenuSave);
		fileMenuPopup.AddItem("Save As...", FileMenuSaveAs);
	}

	public GraphNode CreateRandomizeNode(Vector2 pos)
	{
		var node = randomizeNodeScene.Instantiate<GraphNode>();
		graph.AddChild(node);
		node.PositionOffset = pos;
		return node;
	}

	private void SetupCompatiblePopupMenu(bool fromOutput)
	{
		popupMenu.Clear();

		if (fromOutput)
		{
			// arrastou de um output -> precisa de nodes com input
			popupMenu.AddItem("Add Dialogue", MenuAddDialogueNode);
			popupMenu.AddItem("Add Choices", MenuAddSwitchNode);
			popupMenu.AddItem("Add Random", MenuAddRandomizeNode);
			if (!HasEndNode()) popupMenu.AddItem("Add End", MenuAddEndNode);
		}
		else
		{
			// arrastou de um input -> precisa de nodes com output
			popupMenu.AddItem("Add Dialogue", MenuAddDialogueNode);
			popupMenu.AddItem("Add Choices", MenuAddSwitchNode);
			popupMenu.AddItem("Add Random", MenuAddRandomizeNode);

			if (!HasStartNode())
			{
				popupMenu.AddSeparator();
				popupMenu.AddItem("Add Start", MenuAddStartNode);
			}
		}

		RefreshPreview();
	}

	public override void _ShortcutInput(InputEvent input)
	{
		if (!IsVisibleInTree() || _isBulkLoading || input is not InputEventKey key || !key.Pressed || key.Echo) return;
		var focus = GetViewport().GuiGetFocusOwner();
		if (focus == null || !IsAncestorOf(focus)) return;
		if (!key.CtrlPressed && !key.MetaPressed) return;
		switch (key.Keycode)
		{
			case Key.S: if (key.ShiftPressed) OpenSaveDialog(); else SaveDat(); break;
			case Key.O: OpenLoadDialog(); break;
			case Key.N: RequestReplace(CreateFreshGraph); break;
			default: return;
		}
		GetViewport().SetInputAsHandled();
	}

	private void OnConnectionRequest(StringName fromNodeName, long fromPort, StringName toNodeName, long toPort)
	{
		if (_isBulkLoading) return;
		var from = graph.GetNodeOrNull<DialogueNodeTemplate>(fromNodeName.ToString());
		var to = graph.GetNodeOrNull<DialogueNodeTemplate>(toNodeName.ToString());
		if (!CanConnectNodes(from, to) || fromPort < 0 || fromPort >= from.GetOutputPortCount() || toPort < 0 || toPort >= to.GetInputPortCount()) return;
		// Each response has exactly one target; branching is represented by separate ports.
		foreach (var connection in graph.GetConnectionList())
			if (connection["from_node"].AsStringName() == fromNodeName && connection["from_port"].AsInt32() == fromPort)
				graph.DisconnectNode(fromNodeName, (int)fromPort, connection["to_node"].AsStringName(), connection["to_port"].AsInt32());
		graph.ConnectNode(fromNodeName, (int)fromPort, toNodeName, (int)toPort);
		RefreshPreview();
	}

	private void OnDisconnectionRequest(StringName from, long fromSlot, StringName to, long toSlot)
	{
		graph.DisconnectNode(from, (int)fromSlot, to, (int)toSlot);

		RefreshPreview();
	}

	private void OnConnectionToEmpty(StringName fromNode, long fromSlot, Vector2 releasePosition)
	{
		isCreatingFromConnection = true;
		pendingFromNode = fromNode;
		pendingFromSlot = (int)fromSlot;
		pendingFromOutput = true;

		popupSpawnPosition = (graph.ScrollOffset + releasePosition) / graph.Zoom;

		SetupCompatiblePopupMenu(true);
		ShowGraphPopup(releasePosition);

		RefreshPreview();
	}

	private void OnConnectionFromEmpty(StringName toNode, long toSlot, Vector2 releasePosition)
	{
		isCreatingFromConnection = true;
		pendingFromNode = toNode;
		pendingFromSlot = (int)toSlot;
		pendingFromOutput = false;

		popupSpawnPosition = (graph.ScrollOffset + releasePosition) / graph.Zoom;

		SetupCompatiblePopupMenu(false);
		ShowGraphPopup(releasePosition);

		RefreshPreview();
	}

	private void ShowGraphPopup(Vector2 graphPosition)
	{
		// GraphEdit events are local to the graph. Native popup windows need
		// desktop coordinates; embedded ones need coordinates in their viewport.
		var transform = popupMenu.IsEmbedded() ? graph.GetGlobalTransformWithCanvas() : graph.GetScreenTransform();
		popupMenu.Popup(new Rect2I((Vector2I)(transform * graphPosition), Vector2I.Zero));
	}

	private void OnGraphGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton)
		{
			if (mouseButton.ButtonIndex == MouseButton.Right && mouseButton.Pressed)
			{
				isCreatingFromConnection = false;
				popupSpawnPosition = (graph.ScrollOffset + mouseButton.Position) / graph.Zoom;

				SetupPopupMenu();
				ShowGraphPopup(mouseButton.Position);
				graph.AcceptEvent();
			}
		}

	}

	private void OnFileMenuPressed(long id)
	{
		if (_isBulkLoading) return;
		switch ((int)id)
		{
			case FileMenuNew:
				RequestReplace(CreateFreshGraph);
				break;
			case FileMenuOpen:
				OpenLoadDialog();
				break;
			case FileMenuSave:
				SaveDat();
				break;
			case FileMenuSaveAs:
				OpenSaveDialog();
				break;
		}
	}

	private void OnPopupMenuIdPressed(long id)
	{
		if (_isBulkLoading) return;
		GraphNode createdNode = null;

		switch ((int)id)
		{
			case MenuAddDialogueNode:
				createdNode = CreateDialogueNode(popupSpawnPosition);
				break;

			case MenuAddSwitchNode:
				createdNode = CreateSwitchNode(popupSpawnPosition);
				break;

			case MenuAddStartNode:
				createdNode = CreateStartNodeAt(popupSpawnPosition);
				break;

			case MenuDeleteSelected:
				DeleteSelectedNodes();
				break;

			case MenuDisconnectSelected:
				DisconnectSelectedNode();
				break;

			case MenuAddRandomizeNode:
				createdNode = CreateRandomizeNode(popupSpawnPosition);
				break;

			case MenuAddEndNode:
				createdNode = CreateEndNodeAt(popupSpawnPosition);
				break;
		}

		if (isCreatingFromConnection && createdNode != null)
			ConnectPendingNode(createdNode);

		isCreatingFromConnection = false;

		RefreshPreview();
	}

	private void ConnectPendingNode(GraphNode createdNode)
	{
		if (createdNode is not DialogueNodeTemplate nodeTemplate)
			return;

		if (pendingFromOutput)
		{
			if (!nodeTemplate.HasInput())
				return;

			var fromNode = graph.GetNodeOrNull<DialogueNodeTemplate>(pendingFromNode.ToString());
			var toNode = nodeTemplate;

			if (fromNode == null || toNode == null)
				return;

			if (!CanConnectNodes(fromNode, toNode))
				return;

			OnConnectionRequest(
				pendingFromNode,
				pendingFromSlot,
				createdNode.Name,
				nodeTemplate.GetPrimaryInputSlot()
			);
		}
		else
		{
			if (!nodeTemplate.HasOutput())
				return;

			var fromNode = nodeTemplate;
			var toNode = graph.GetNodeOrNull<DialogueNodeTemplate>(pendingFromNode.ToString());

			if (fromNode == null || toNode == null)
				return;

			if (!CanConnectNodes(fromNode, toNode))
				return;

			OnConnectionRequest(
				createdNode.Name,
				nodeTemplate.GetPrimaryOutputSlot(),
				pendingFromNode,
				pendingFromSlot
			);
		}

		RefreshPreview();
	}

	private void DisconnectSelectedNode()
	{
		GraphNode selectedNode = GetFirstSelectedNode();

		if (selectedNode == null)
			return;

		RemoveConnectionsFromNode(selectedNode);

		RefreshPreview();
	}

	private void LoadPreviewDat()
	{
		LoadDat(DefaultDialoguePath);
	}

	private bool HasIncomingConnection(StringName nodeName)
	{
		foreach (Godot.Collections.Dictionary connection in graph.GetConnectionList())
		{
			StringName toNode = (StringName)connection["to_node"];

			if (toNode == nodeName)
				return true;
		}

		return false;
	}

	private bool CanConnectNodes(DialogueNodeTemplate fromNode, DialogueNodeTemplate toNode)
	{
		if (fromNode == null || toNode == null)
			return false;

		if (fromNode == toNode)
			return false;

		if (toNode.NodeType == "start")
			return false;

		if (fromNode.NodeType == "end")
			return false;

		return true;
	}

	private void DeleteSelectedNodes()
	{
		var nodesToDelete = new Godot.Collections.Array<GraphNode>();

		foreach (Node child in graph.GetChildren())
		{
			if (child is GraphNode graphNode && graphNode.Selected)
			{
				if (child is DialogueNodeTemplate baseNode && baseNode.NodeType == "start")
					continue;

				nodesToDelete.Add(graphNode);
			}
		}

		foreach (GraphNode node in nodesToDelete)
		{
			RemoveConnectionsFromNode(node);

			if (node.GetParent() == graph)
				graph.RemoveChild(node);

			node.QueueFree();
		}

		RefreshPreview();
	}

	private void RemoveConnectionsFromNode(GraphNode node)
	{
		var connections = new List<Godot.Collections.Dictionary>();

		foreach (Godot.Collections.Dictionary connection in graph.GetConnectionList())
			connections.Add(connection);

		foreach (var connection in connections)
		{
			StringName fromNode = (StringName)connection["from_node"];
			int fromPort = (int)(long)connection["from_port"];
			StringName toNode = (StringName)connection["to_node"];
			int toPort = (int)(long)connection["to_port"];

			if (fromNode == node.Name || toNode == node.Name)
				graph.DisconnectNode(fromNode, fromPort, toNode, toPort);
		}
	}

	private void ApplyNodeData(DialogueNodeTemplate node, DalgeData data)
	{
		switch (data.Type)
		{
			case "dialogue":
				if (node is DialogueNode dialogueNode)
				{
					dialogueNode.SetText(data.Text);
					dialogueNode.SetOption1Text(GetOptionText(data, 0));
					dialogueNode.SetOption2Text(GetOptionText(data, 1));
				}
				break;

			case "start":
				break;

			case "end":
				break;

			case "switch":
				if (node is SwitchNode switchNode)
				{
					for (int i = 0; i < 3; i++)
						switchNode.SetOptionText(i, GetOptionText(data, i));
				}
				break;

			case "randomize":
				break;
		}
	}

	private void CreateFreshGraph()
	{
		ClearGraph();
		CreateStartNodeAt(new Vector2(80, 100));
		CreateEndNodeAt(new Vector2(560, 100));
		SetCurrentFilePath("", false);
		_savedContent = BuildDat();
		RefreshPreview();
		FrameGraph();
	}

	public void CreateStartNode()
	{
		CreateStartNodeAt(new Vector2(80, 100));

		RefreshPreview();
	}

	public GraphNode CreateStartNodeAt(Vector2 pos)
	{
		if (HasStartNode())
			return null;

		var node = startNodeScene.Instantiate<GraphNode>();
		graph.AddChild(node);
		node.PositionOffset = pos;

		RefreshPreview();
		return node;
	}

	public GraphNode CreateDialogueNode(Vector2 pos)
	{
		var node = dialogueNodeScene.Instantiate<GraphNode>();
		graph.AddChild(node);
		node.PositionOffset = pos;

		RefreshPreview();
		return node;
	}

	public GraphNode CreateSwitchNode(Vector2 pos)
	{
		var node = switchNodeScene.Instantiate<GraphNode>();
		graph.AddChild(node);
		node.PositionOffset = pos;

		RefreshPreview();
		return node;
	}

	private static string GetOptionText(DalgeData data, int index)
	{
		return data.OptionTextByIndex.TryGetValue(index, out var text) ? text : string.Empty;
	}

	private void OpenLoadDialog()
	{
		RequestReplace(() =>
		{
			openFileDialog.CurrentPath = string.IsNullOrWhiteSpace(_currentFilePath) ? "res://dialogues/" : _currentFilePath;
			openFileDialog.PopupCenteredRatio();
		});
	}

	private void OpenSaveDialog()
	{
		saveFileDialog.CurrentPath = string.IsNullOrWhiteSpace(_currentFilePath) ? "res://dialogues/untitled.dalge" : _currentFilePath;
		saveFileDialog.PopupCenteredRatio();
	}

	private void OnOpenFileSelected(string path)
	{
		LoadDat(path);
	}

	private void OnSaveFileSelected(string path)
	{
		SaveDatToPath(path);
	}

	private void OnFilePathSubmitted(string newText)
	{
		SetCurrentFilePath(newText);
		UpdateStatus($"Selected path: {_currentFilePath}");
	}

	private void LoadCurrentPath()
	{
		RequestReplace(() => LoadDat(GetValidatedPathInput()));
	}

	private void SetCurrentFilePath(string path, bool updateStatus = true)
	{
		_currentFilePath = path?.Trim() ?? "";
		if (filePathEdit != null)
			filePathEdit.Text = _currentFilePath;

		if (updateStatus)
			UpdateStatus($"Selected path: {_currentFilePath}");
	}

	private string GetValidatedPathInput()
	{
		var rawPath = filePathEdit?.Text?.Trim();
		return string.IsNullOrWhiteSpace(rawPath) ? _currentFilePath : rawPath;
	}

	private void UpdateStatus(string message)
	{
		if (statusLabel != null)
			statusLabel.Text = message;
	}

	private static string GetFileNameFromPath(string path)
	{
		return string.IsNullOrWhiteSpace(path) ? "Untitled.dalge" : path.GetFile();
	}
}
